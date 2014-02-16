# Cordova-SQLitePlugin-WP

### Apache Cordova/Phonegap SQLite plugin for Windows Phone 8+

> This plugin is an HTML5 Web SQL polyfill for Windows Phone 8+ Apache Cordova applications.

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

* Tests and Examples ([Cordova-SQLitePlugin-WP-Examples](https://github.com/welldone-software/Cordova-SQLitePlugin-WP-Examples))
 
 	* Fully working example project using Apache Cordova command line

 	* Fully working example project using manual installation

 	* Extensive Jasmine 2.0 based test suite 


## Installation

You can install the plugin using the Apache Cordova command line or manually.

### Using the Cordova command line 

Assuming you have installed the Apache Cordova cli, created a cordova project and added the `wp8` platform (see [Apache Cordova command-line interface docs](http://cordova.apache.org/docs/en/3.3.0/guide_cli_index.md.html)), cd to the Cordova project root and run:

```bash
cordvova plugins add Cordova-SQLitePlugin-WP
``` 

### Manual installation

1. Create a Cordova wp8 Visual Studio project using the wizard (see [Apache Cordova's Windows Phone 8 Platform Guide](http://cordova.apache.org/docs/en/3.3.0/guide_platforms_wp8_index.md.html)).

1. Clone this repository or download a ziped vesrion and unzip it.

1. Copy the `src/wp/*.cs` to your Visual Studio project directory.

1. Add the `www/*.js` to your www directory.

1. Add the below xml fragment to your `config.xml` file

```xml
	<config-file target="config.xml" parent="/*">
	    <feature name="SQLitePlugin">
	        <param name="wp-package" value="SQLitePlugin"/>
	    </feature>
	</config-file>
```

## Usage

Working examples for both a manual constrcted and cli based projects are available at [Cordova-SQLitePlugin-WP-Examples](https://github.com/welldone-software/Cordova-SQLitePlugin-WP-Examples).

Once the plugin is installed, the following jasmine 2.0 test should succeed:

```js
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
```

## License

```
Copyright (c) 2014 Welldone Software Solutions Ltd.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```