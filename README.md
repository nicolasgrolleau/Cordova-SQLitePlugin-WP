# Cordova-SQLitePlugin-WP

### Cordova/Phonegap SQLite plugin for Windows Phone 8+

> This plugin is an HTML5 Web SQL polyfill for Windows Phone 8+ cordova applications.

## Featuers

* HTML5 Web SQL syntax and semantics
	* With the excepton of `window.openDatabase` and `db.close` being asyncronous, accepting callbacks for success and error. 

* Multiple opened databases and outstanding transactions

* Full support for nesting `executeSql` in a trasaction
	
	* Combine nested and non-nested `executeSql` calls in a single transaction.

	* Data and error callbacks allow further `executeSql` calls to be made from within them.

	* The data callabck returns a `resultSet` with `items`, `rowsAffected` and `insertId`.

	* The error callabck allows cancelling the rollback by returning `false`.
	
	
* Versioning support

	* Version argument is enforced in `window.openDatabse` 
		* If the databse exists, method fail if requested version is wrong.
		* If it doesn't, a new one is created and assigned the requrested version.
		* Version argument can be an empty string `''`
			* If the databse exists, in any version, will open and set the version.
			* If it doesn't, will create a new one with version '0.0'.

	* Databse version is availbale via `db.version` property.

	* Support for transactional version changes via `db.changeVersion`.

* Test suites

	* All based on jasmine.

	* Unit tests - js only specs 

	* Integraiton - e2e specs (js + native)

	* Stress tests - (js + native)


## Installation

You can install the plugin using the cordova command line or manually.

### Using the cordova command line 

Assuming you have installed the cordova cli and created a wp8 project, cd to its root and execute:

```bash
cordvova plugins add Cordova-SQLitePlugin-WP
```

### Manual installation

1. Create a cordova wp8 project using the templates or Visual Studio wizards.

1. Clone this repository or download a ziped vesrion and unzip it.

1. Copy the `src/wp/*.cs` to your Visual Studio project directory.

1. Add the `www/*.js` to your www directory.

1. Add the below xml fragment to your `config.xml` file

```xml
<<<<<<< HEAD
=======

>>>>>>> b712050634d33dbeb4d75f5dd48a97917961b4c7
	<config-file target="config.xml" parent="/*">
	    <feature name="SQLitePlugin">
	        <param name="wp-package" value="SQLitePlugin"/>
	    </feature>
	</config-file>
<<<<<<< HEAD
=======

>>>>>>> b712050634d33dbeb4d75f5dd48a97917961b4c7
```

## Usage

Working examples for both a manual constrcted and cli based projects are available at [Cordova-SQLitePlugin-WP-Examples](https://github.com/welldone-software/Cordova-SQLitePlugin-WP-Examples){:target="_blank"}.

Once the plugin is installed, the following jasmine 2.0 test should succeed:

```js
<<<<<<< HEAD
=======

>>>>>>> b712050634d33dbeb4d75f5dd48a97917961b4c7
it('sould allow crud and database managment scripts', function (done) {
    window.openDatabase('name', '1.0', 'desc', 1024 * 1024 * 5, function (db) {
        db.transaction(
        function (tx) {
            tx.executeSql('CREATE TABLE t(x INTEGER PRIMARY KEY ASC, yy TEXT, zz INTEGER)');
            tx.executeSql('DROP TABLE t');
            tx.executeSql('CREATE TABLE t(x INTEGER PRIMARY KEY ASC, y TEXT, z INTEGER, k INTEGER)');
            tx.executeSql('INSERT INTO t (y, z, k) VALUES("y", 7, 2)');
            tx.executeSql('INSERT INTO t (y, z, k) VALUES("y1", 71, 105)');
            tx.executeSql('SELECT COUNT(x) as c FROM t', [], function (t, rs) {
                expect(rs.rows.length).toBe(1);
                var item = rs.rows.item(0);
                expect(item['c']).toBe(2);
                done();
            });
        },
        function (err) {
            expect('Unexpected error ' + JSON.stringify(err)).toBeUndefined();
            done();
        });
    });
});

<<<<<<< HEAD
=======

>>>>>>> b712050634d33dbeb4d75f5dd48a97917961b4c7
```