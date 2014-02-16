//
// Copyright (c) 2014 Welldone Software Solutions Ltd.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SQLite;
using WPCordovaClassLib.Cordova;
using WPCordovaClassLib.Cordova.Commands;

namespace Cordova.Extension.Commands
{
    /// <summary>
    ///     Implementes access to SQLite DB
    /// </summary>
    public class SQLitePlugin : BaseCommand
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly IDictionary<string, SQLiteConnection> _connections = new Dictionary<string, SQLiteConnection>();

        public void open(string json)
        {
            var args = GetArgument<OpenArgs>(json);
            SQLiteConnection connection = null;

            try
            {
                if (_connections.ContainsKey(args.Name))
                {
                    //todo: fail or be robust and use existing???
                    throw new Exception("Database allready open");
                }

                int? reqeustedVersion = args.Version;
                string dbPath = args.Name;
                bool isNew = !IsolatedStorageFile.GetUserStoreForApplication().FileExists(dbPath);

                connection = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create, true);

                if (isNew)
                {
                    if (reqeustedVersion.HasValue)
                    {
                        connection.CreateCommand(string.Format("PRAGMA user_version = {0};", reqeustedVersion.Value))
                            .ExecuteNonQuery();
                    }
                }

                var existingVersion = connection.CreateCommand("PRAGMA user_version;").ExecuteScalar<int>();

                if (reqeustedVersion.HasValue && existingVersion != reqeustedVersion)
                {
                    throw new Exception(string.Format("Wrong database version{2}. Requested {0} and existings {1}.",
                        reqeustedVersion, existingVersion, isNew ? " for new db" : ""));
                }

                _connections[args.Name] = connection;

                DispatchSuccess(new {Version = existingVersion});
            }
            catch (Exception e)
            {
                if (connection != null)
                {
                    connection.Dispose();
                }
                DispatchError(connection, e);
            }
        }

        public void close(string json)
        {
            var args = GetArgument<CloseArgs>(json);

            SQLiteConnection connection;
            if (_connections.TryGetValue(args.Name, out connection))
            {
                _connections.Remove(args.Name);
                try
                {
                    connection.Dispose();
                }
                catch (Exception)
                {
                    //todo: log... but ignore
                }

                DispatchSuccess();
            }
            else
            {
                DispatchError(connection, new Exception("Database '" + args.Name + "' is not open."));
            }
        }

        public void forceEndTransaction(string json)
        {
            var args = GetArgument<EndTransactionArgs>(json);
            SQLiteConnection connection = null;
            try
            {
                if (!_connections.TryGetValue(args.Name, out connection))
                {
                    throw new Exception("Database is not open");
                }

                if (!connection.IsInTransaction)
                {
                    throw new Exception("Database not in tranaction");
                }

                if (args.IsCommit)
                {
                    if (args.NewVersion != null)
                    {
                        try
                        {
                            connection.CreateCommand(string.Format("PRAGMA user_version = {0};", args.NewVersion.Value))
                                .ExecuteNonQuery();
                            var existingVersion = connection.CreateCommand("PRAGMA user_version;").ExecuteScalar<int>();
                            if (existingVersion != args.NewVersion)
                            {
                                throw new Exception(string.Format("Could no set version to {0} for '{1}'. It is {2}",
                                    args.NewVersion, args.Name, existingVersion));
                            }
                        }
                        catch (Exception)
                        {
                            connection.Rollback(); //todo: protect from rollback error
                            throw;
                        }
                    }

                    connection.Commit();
                }
                else
                {
                    connection.Rollback();
                }

                if (connection.IsInTransaction)
                {
                    throw new Exception("Failed to end tranaction");
                }

                DispatchSuccess();
            }
            catch (Exception e)
            {
                DispatchError(connection, e);
            }
        }

        public void runBatch(string json)
        {
            SQLiteConnection connection = null;

            try
            {
                var args = GetArgument<RunBatchArgs>(json);

                if (!_connections.TryGetValue(args.Name, out connection))
                {
                    throw new Exception("Database is not open");
                }

                object resultSet = DoRunBatch(connection, args);

                DispatchSuccess(new {resultSet, connection.IsInTransaction});
            }
            catch (Exception e)
            {
                DispatchError(connection, e);
            }
        }

        private object DoRunBatch(SQLiteConnection connection, RunBatchArgs args)
        {
            if (args.IsFirstBatch)
            {
                if (connection.IsInTransaction)
                {
                    throw new Exception(string.Format("'{0}' allready in trascation", args.Name));
                    //todo: just connection.Rollback(); and be more robust?
                }

                if (args.RequiredVersion != null)
                {
                    var existingVersion = connection.CreateCommand("PRAGMA user_version;").ExecuteScalar<int>();
                    if (existingVersion != args.RequiredVersion)
                    {
                        throw new Exception(string.Format("Unexpected version {0} for '{1}' while requesting {2}",
                            existingVersion, args.Name, args.RequiredVersion));
                    }
                }

                connection.BeginTransaction();
            }

            if (!connection.IsInTransaction)
            {
                throw new Exception(string.Format("'{0}' not in trascation", args.Name));
            }

            object resultSet = null;
            SqlStatement lastStatement = args.Statements.Last();

            foreach (SqlStatement statement in args.Statements)
            {

                    try
                    {
                        List<SQLiteQueryRow> rows = null;
                        if (!CheckAndRunDropTableWorkArroung(statement, connection))
                        {
                            rows = connection.Query2(statement.Sql, statement.Args ?? new object[] { });
                        }

                        if (statement == lastStatement)
                        {
                            resultSet =
                                new
                                {
                                    Rows = rows ?? new List<SQLiteQueryRow>(),
                                    RowsAffected = connection.Handle.nChange,
                                    InsertId = connection.Handle.lastRowid
                                };
                        }
                    }
                    catch (Exception e)
                    {
                        if (statement != lastStatement || !args.MayRecoverFromError)
                        {
                            connection.Rollback(); //todo: protect from rollback error
                        }
                        throw;
                    }
                }
            

            if (!args.MayNotBeLastBatch)
            {
                connection.Commit();
            }

            return resultSet;
        }

        private bool CheckAndRunDropTableWorkArroung(SqlStatement statement, SQLiteConnection connection)
        {
            var stmts = SplitDropTableStatment(statement);
            if (stmts != null)
            {
                foreach (var stmt in stmts)
                {
                    connection.Query2(stmt.Sql, stmt.Args ?? new object[] { });
                }
            }
            return stmts != null;
        }

        private IEnumerable<SqlStatement> SplitDropTableStatment(SqlStatement statement)
        {
            Match match = Regex.Match(statement.Sql, @"^\s*DROP\s+TABLE\s+(?<IfExists>IF\s+EXISTS\s+)?(?<TableName>\S+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var statements = new List<SqlStatement>();
            var tableName = match.Groups["TableName"].Value;

            if (match.Groups["IfExists"].Success)
            {
                statements.Add(new SqlStatement{Sql = string.Format("CREATE TABLE IF NOT EXISTS {0} (x INTEGER)", tableName)});
            }

            statements.Add(new SqlStatement{Sql = string.Format("DELETE FROM {0}", tableName)});
            statements.Add(
                new SqlStatement{Sql = string.Format("ALTER TABLE {0} RENAME TO _DEL_{1}", tableName, DateTime.Now.Ticks)});

            return statements;
        }

        private T GetArgument<T>(string json)
        {
            string str = JsonConvert.DeserializeObject<string[]>(json)[0];
            return JsonConvert.DeserializeObject<T>(str, JsonSettings);
        }

        private void DispatchResult(object result, bool sucess)
        {
            if (result is string)
            {
                result = new {Message = (string) result};
            }

            string message = result != null ? JsonConvert.SerializeObject(result, JsonSettings) : null;
            PluginResult.Status status = sucess ? PluginResult.Status.OK : PluginResult.Status.ERROR;
            Debug.WriteLine("result status {0} - mesage {1}", status, message);
            DispatchCommandResult(new PluginResult(status, message));
        }

        protected void DispatchSuccess(object result = null)
        {
            DispatchResult(result, true);
        }

        protected void DispatchError(object error, bool inInTx, bool hasDb)
        {
            for (var ex = error as Exception; ex != null; ex = ex.InnerException)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
            }
            DispatchResult(error, false);
        }

        private void DispatchError(SQLiteConnection connection, Exception e)
        {
            bool hasDatabase = connection != null;
            bool isInTransaction = hasDatabase && connection.IsInTransaction;
            //todo: log the error with stack
            DispatchError(e, isInTransaction, hasDatabase);
        }

        private class CloseArgs
        {
            public string Name { get; set; }
        }

        private class EndTransactionArgs
        {
            public string Name { get; set; }
            public bool IsCommit { get; set; }
            public int? NewVersion { get; set; }
        }

        private class OpenArgs
        {
            public string Name { get; set; }
            public int? Version { get; set; }
        }

        private class RunBatchArgs
        {
            public string Name { get; set; }
            public bool IsFirstBatch { get; set; }
            public int? RequiredVersion { get; set; }
            public bool MayRecoverFromError { get; set; }
            public bool MayNotBeLastBatch { get; set; }
            public string TransactionId { get; set; }
            public SqlStatement[] Statements { get; set; }
        }

        public class SqlStatement
        {
            public string Sql { get; set; }
            public object[] Args { get; set; }
        }
    }
}