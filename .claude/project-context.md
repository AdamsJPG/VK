## Current Project

Compare 2 MSSQL Databases for changes in columns and data (subject to exclusions)

### Concept

A windows application which will allow the configuring of two databases, after connecting to both databases, the tool will determine all tables, columns and data and compare. We then need to produce an artifact showing the differences. This tool is similar to SQL data compare, but will exclude timestamps, uuids, and ids.

The idea will be that we run the same series of actions on two different systtems (**A** and **B** - **A** is the original application and **B** is a refactored version of the presentation layer only. What is written to the database will be identical.)

### Scope assumptions

- Both databases are Microsoft SQL databases with appropriate user permissions to be accessed.
- This will be an installable windows application.

### Restrictions

The tool will not consider difference in timestamps or uuids.
