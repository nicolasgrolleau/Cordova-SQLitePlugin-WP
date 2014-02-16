using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Media;
using Windows.Storage.Streams;
using Microsoft.Phone.Scheduler;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SQLite;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using System.Collections.ObjectModel;
using WPCordovaClassLib.Cordova;
using WPCordovaClassLib.Cordova.Commands;
using WPCordovaClassLib.Cordova.JSON;
using System.IO;
using Windows.Storage;
using System.Text.RegularExpressions;
using System.IO.IsolatedStorage;

namespace Cordova.Extension.Commands
{
    /// <summary>
    /// Implementes access to SQLite DB
    /// </summary>
    public class SQLitePlugin : BaseCommand
    {
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

                int existingVersion = connection.CreateCommand("PRAGMA user_version;").ExecuteScalar<int>();

                if (reqeustedVersion.HasValue && existingVersion != reqeustedVersion)
                {
                    throw new Exception(string.Format("Wrong database version{2}. Requested {0} and existings {1}.",
                        reqeustedVersion, existingVersion, isNew ? " for new db" : ""));
                }

                _connections[args.Name] = connection;

                DispatchSuccess(new { Version = existingVersion });

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
                            int existingVersion = connection.CreateCommand("PRAGMA user_version;").ExecuteScalar<int>();
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
            //Debug.WriteLine("runBatch " + json);
            SQLiteConnection connection = null;

            try
            {
                var args = GetArgument<RunBatchArgs>(json);

                if (!_connections.TryGetValue(args.Name, out connection))
                {
                    throw new Exception("Database is not open");
                }

                object resultSet = DoRunBatch(connection, args);

                DispatchSuccess(new { resultSet, connection.IsInTransaction });
            }
            catch (Exception e)
            {
                DispatchError(connection, e);
            }
        }

        private object DoRunBatch(SQLiteConnection connection, RunBatchArgs args)
        {
            // Debug.WriteLine("DoRunBatch " + args.Name + );
            if (args.IsFirstBatch)
            {
                if (connection.IsInTransaction)
                {
                    throw new Exception(string.Format("'{0}' allready in trascation", args.Name));
                    //todo: just connection.Rollback(); and be more robust?
                }

                if (args.RequiredVersion != null)
                {
                    int existingVersion = connection.CreateCommand("PRAGMA user_version;").ExecuteScalar<int>();
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

            foreach (SqlStatement rawStatement in args.Statements)
            {
                bool lastRaw = rawStatement == args.Statements.Last();
                SqlStatement[] statementsToRun = getStatementsToRun(rawStatement);

                foreach (SqlStatement statement in statementsToRun)
                {
                    bool lastStatement = lastRaw && statement == statementsToRun.Last();

                    try
                    {
                        var rows = connection.Query2(statement.Sql, statement.Args ?? new object[] {});

                        if (lastStatement)
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
                        if (lastStatement || !args.MayRecoverFromError)
                        {
                            connection.Rollback(); //todo: protect from rollback error
                        }
                        throw e;
                    }
                }
            }

            if (!args.MayNotBeLastBatch)
            {
                connection.Commit();
            }

            return resultSet;
        }

        private SqlStatement[] getStatementsToRun(SqlStatement statement)
        {
            return dropTableWorkAround(statement);
        }

        private SqlStatement[] dropTableWorkAround(SqlStatement statement)
        {
            Match match = Regex.Match(statement.Sql, @"^DROP\s+TABLE\s+(IF\s+EXISTS\s+|)(\S+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return new SqlStatement[]{statement};
            }

            bool ifExists = (match.Groups[1].Value != "");
            string tableName = match.Groups[2].Value;
            var nowMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            List<SqlStatement> statements = new List<SqlStatement>();
            if (ifExists)
            {
                statements.Add(new SqlStatement(string.Format("CREATE TABLE IF NOT EXISTS {0} (x INTEGER)", tableName)));
            }

            statements.Add(new SqlStatement(string.Format("DELETE FROM {0}", tableName)));
            statements.Add(new SqlStatement(string.Format("ALTER TABLE {0} RENAME TO _DEL_TABLE_{0}_{1}", tableName, nowMilliseconds)));

            return statements.ToArray();
        }

        private class OpenArgs
        {
            public string Name { get; set; }
            public int? Version { get; set; }
        }

        private class EndTransactionArgs
        {
            public string Name { get; set; }
            public bool IsCommit { get; set; }
            public int? NewVersion { get; set; }
        }

        private class CloseArgs
        {
            public string Name { get; set; }
        }

        private class RunBatchArgs
        {
            public string Name { get; set; }
            public bool IsFirstBatch { get; set; }
            public int? RequiredVersion { get; set; }
            public bool MayRecoverFromError { get; set; }
            public bool MayNotBeLastBatch { get; set; }
            public string TransactionId { get; set; }

            //public List<SqlStatement> Statements { get; set; }
            public SqlStatement[] Statements { get; set; }

            //public double? ExpectedSize { get; set; }
            //public string DisplayName { get; set; }
        }

        public class SqlStatement
        {
            public SqlStatement(string Sql)
            {
                this.Sql = Sql;
            }

            public string Sql { get; set; }
            public object[] Args { get; set; }
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private T GetArgument<T>(string json)
        {
            var str = JsonConvert.DeserializeObject<string[]>(json)[0];
            Debug.WriteLine(json);
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
    }
}