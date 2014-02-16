(function () {

    var cdv = window.cordova || window.Cordova || PhoneGap,
        isPluginBusy = false,
        busyRetryInterval = 5,
        emptyFn = function () {
        },
        VersionFormat = {
            isValid: function (versionStr) {

                //Version must be in the format: D{ddd}.d{D} and 0.0. D is 1-9 and d is 0-9. {} marks optional places.
                // 1.0, 44.02, 3657.71 are allowed. 1.00, 0.2, 2.20, 0657.5 are not allowed.

                var valid = typeof(versionStr) == 'string'
                    && (versionStr === '' || versionStr === '0.0' || /^[1-9][0-9]{0,3}\.[0-9][1-9]?$/.test(versionStr));

                return valid;
            },
            parse: function (versionStr) {
                if (!this.isValid(versionStr)) {
                    throw new Error('Invalid version format: ' + versionStr);
                }
                return versionStr ? Math.floor(Number(versionStr) * 100) : null;
            },
            format: function (intVersion) {
                var version = (Number(intVersion) / 100).toString();
                if (version.indexOf('.') == -1) {
                    version += '.0';
                }
                return version;
            }
        };

    function chain() {
        var fns = Array.prototype.splice.call(arguments, 0);
        return function () {
            for (var fn = fns.pop(); fn; fn = fns.pop()) {
                if (typeof (fn) == 'function') {
                    fn.apply(null, arguments);
                }
            }
        };
    }

    function trim(input, ch) {
        ch = ch || '\s';
        return input.replace(/^(' + ch + ')+|(' + ch + ')+$/gm, '');
    }

    function serializedExec(method, args, successCallback, errorCallback) {
        var turnOffBusyFlag = function () {
            isPluginBusy = false;
        };

        if (isPluginBusy) {
            window.setTimeout(function () {
                serializedExec(method, args, successCallback, errorCallback);
            }, busyRetryInterval);
            return undefined;
        }

        isPluginBusy = true;

        return cdv.exec(chain(turnOffBusyFlag, successCallback), chain(turnOffBusyFlag, errorCallback), 'SQLitePlugin', method, [args]);
    }


    function fixResultSet(resultSet) {

        var rows = resultSet.rows.map(function (r) {
            var row = {};
            r.column.forEach(function (c) {
                row[c.key] = c.value;
            });
            return row;
        });

        resultSet.rows = {
            item: function (idx) {
                return rows[idx];
            },
            length: rows.length
        };

        return resultSet;
    }

    function Database(name, version) {

        var me = this;

        me.name = name;
        me.version = version;
        me.used = false;
        me.pendingTransactions = [];
        me.runningTransaction = null;

        me.externalInterface = {
            close: me.close.bind(me),
            transaction: me.transaction.bind(me),
            changeVersion: me.changeVersion.bind(me),
            get version() {
                return me.version;
            },
            get name() {
                return me.name;
            }
        }
    }

    Database.availableDatabases = {};

    Database.prototype.changeVersion = function (oldVersion, newVersion, callback, errorCallback, successCallback) {
        oldVersion = VersionFormat.parse(oldVersion);
        newVersion = VersionFormat.parse(newVersion);
        this.enqueueTransaction(callback, oldVersion, newVersion, errorCallback, successCallback);
    };

    Database.prototype.transaction = function (callback, errorCallback, successCallback) {
        this.enqueueTransaction(callback, null, null, errorCallback, successCallback);
    };

    Database.prototype.enqueueTransaction = function (action, fromVersion, toVersion, txErrorCallback, txSuccessCallback) {
        try {
            if (Database.availableDatabases[this.name] != this) {
                throw new Error('Database connection closed. Use openDatabase first');
            }

            var execNext = (function () {
                this.runningTransaction = null;
                this.runPending();
            }).bind(this),
                transaction = new Transaction(this, action, fromVersion, toVersion, chain(txErrorCallback, execNext), chain(txSuccessCallback, execNext));
            this.pendingTransactions.push(transaction);
            this.runPending();
        }
        catch (e) {
            if (txErrorCallback) {
                txErrorCallback(e);
            } else {
                throw e;
            }
        }
    };

    Database.prototype.runPending = function () {

        if (this.runningTransaction) {
            return;
        }
        this.runningTransaction = this.pendingTransactions.shift();
        if (!this.runningTransaction) {
            return;
        }
        this.runningTransaction.start();
    };

    Database.prototype.close = function (successCallback, errorCallback) {
        if (this.runningTransaction || this.pendingTransactions.length) {
            throw new Error('Transaction in process');
        }
        delete Database.availableDatabases[this.name];
        //todo: wait to reply befor removing from list? log/throw any error?
        serializedExec('close', {name: this.name}, successCallback, errorCallback);
    };


    function Transaction(conn, action, fromVersion, toVersion, txErrorCallback, txSuccessCallback) {
        this.id = 'tx' + Transaction.idSeed++;

        this.action = action;
        this.database = conn;
        //this.ended = false;
        this.started = false;
        this.pendingStatements = [];
        this.txErrorCallback = txErrorCallback || emptyFn;
        this.txSuccessCallback = txSuccessCallback || emptyFn;
        this.fromVersion = fromVersion;
        this.toVersion = toVersion;

        this.externalInterface = { executeSql: this.executeSql.bind(this) };
    }

    Transaction.idSeed = 0;

    Transaction.prototype.executeSql = function (sql, args, dataCallback, errorCallback) {
        this.pendingStatements.push({
            sql: sql,
            args: args,
            dataCallback: dataCallback,
            errorCallback: errorCallback
        });
    };

    Transaction.prototype.start = function () {
        this.action(this.externalInterface);

        if (this.pendingStatements.length == 0) {
            if(Boolean(this.toVersion)){
                this.executeSql("select 1", []);
            }
            else{
                this.end();
                return;
            }
        }

        this.nextBatch();
    };

    Transaction.prototype.nextBatch = function () {
        var batch = [];
        for (var s = this.pendingStatements.shift(); s; s = this.pendingStatements.shift()) {
            batch.push(s);
            if (s.dataCallback || s.errorCallback) {
                break;
            }
        }
        var lastStmt = batch[batch.length - 1],
            args = {
                name: this.database.name,
                transactionId: this.id,
                isFirstBatch: !this.started,
                requiredVersion: this.fromVersion,
                mayRecoverFromError: Boolean(lastStmt.errorCallback),
                //todo: expose hasErrorCallback and hasDataCallback to native instead of these
                mayNotBeLastBatch:
                    Boolean(lastStmt.dataCallback)
                    || Boolean(lastStmt.errorCallback)
 //                   || this.pendingStatements.length > 0
                    || Boolean(this.toVersion),
                statements: batch.map(function (p) {
                    return {sql: p.sql, args: p.args};
                })
            },
            success = function (result) {
                if (lastStmt.dataCallback) {
                    lastStmt.dataCallback(this.externalInterface, fixResultSet(result.resultSet)); //todo: try catch on call
                }
                if (this.pendingStatements.length > 0) {
                    this.nextBatch();
                }
                else {
                    //todo: assert still in transaction if mayNotBeLastBatch
                    this.end(result.isInTransaction === true);
                }
            },
            error = function (error) {
                console.log('error from plugin: ' + error);
                if (lastStmt.errorCallback) {
                    if (lastStmt.errorCallback(this.externalInterface, error) === false) { //todo: try catch on call
                        if (this.pendingStatements.length > 0) {
                            this.nextBatch();
                        }
                        else {
                            //todo: assert error.isInTransaction since mayNotBeLastBatch should be true
                            this.end(true); //commit as if success
                        }
                        return;
                    }
                }
                //todo: assert still in transaction if mayNotBeLastBatch
                this.end(error.isInTransaction === true, error);
            };

        this.started = true;
        this.database.used = true;

        return serializedExec('runBatch', args, success.bind(this), error.bind(this));
    };

    Transaction.prototype.end = function(isInTransaction, error) {

        var notifyTxResult = function(newError) {
            error = error || newError;
            this[error ? 'txErrorCallback' : 'txSuccessCallback'](error);
        }.bind(this);

        if (isInTransaction) {
            var args = {
                isCommit: !error,
                newVersion: this.toVersion,
                name: this.database.name,
                transactionId: this.id
            },
                updateVersion = function() {
                    if (args.isCommit == true && args.newVersion) {
                        this.database.version = VersionFormat.format(args.newVersion);
                    }
                }.bind(this);

            serializedExec("forceEndTransaction", args,
                function(result) {
                    updateVersion();
                    notifyTxResult();
                },
                function(err) {
                    //todo: log, this is may cause us inconsistencies
                    notifyTxResult(err);
                });
        } else {
            notifyTxResult();
        }
    };

    window.openDatabase = function (name, requestedVersion, displayName, maxSize, successCallback, errorCallback) {
        if (typeof(successCallback) != 'function' || typeof(errorCallback) != 'function') {
            throw new Error('Open database is asynchronous and requires both a success and error callback functions.');
        }
        if (!name) {
            throw new Error('Name cannot be empty');
        }
        if (requestedVersion == null || requestedVersion == undefined) {
            throw new Error('Version must be specified. For no requestedVersion, use an empty string');
        }

        if (!VersionFormat.isValid(requestedVersion)) {
            throw new Error('Invalid version format for ' + requestedVersion);
        }

        if (Database.availableDatabases[name]) {
            errorCallback(new Error('Database already open.'));
            return;
        }

        Database.availableDatabases[name] = {};

        serializedExec('open', {name: name, version: VersionFormat.parse(requestedVersion)},
            function (info) {
                var db = new Database(name, VersionFormat.format(info.version) /*, displayName, maxSize*/);

                Database.availableDatabases[name] = db;

                successCallback(db.externalInterface);
            },
            function (err) {
                console.log('return error for ' + name + ' - ' + JSON.stringify(err));
                delete Database.availableDatabases[name];
                errorCallback(err);
            });
    };

})();