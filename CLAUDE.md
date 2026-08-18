# CLAUDE.md

Compare 2 MSSQL Databases for changes in columns and data (subject to exclusions)

## Project Overview

A windows application which will allow the configuring of two databases, after connecting to both databases, the tool will determine all tables, columns and data and compare. We then need to produce an artifact showing the differences. This tool is similar to SQL data compare, but will exclude timestamps, uuids, and ids.

## Session memory — start of session

At the start of each session, read these files in order:

1. `.claude\project-context.md` — current project state
2. `.claude\session-log.md` — recent session history

## End of session — wrap up procedure

When I say "wrap up" or "end session":

1. Generate a session log entry with this structure:

   ## YYYY-MM-DD

   **Goal:** [what we set out to do]
   **Done:** [what was actually completed]
   **Decisions:** [any architectural or approach decisions made]
   **Left to do:** [remaining work]
   **Patterns noted:** [anything that improved or hurt output quality]

2. Prepend this entry to the TOP of `.claude\session-log.md` using the Edit tool (insert the new section before the first existing `## ` heading). If the file doesn't exist yet, create it with this entry as the first section.

### Coding Standards

## 1. Control-Flow Bracing — No Single-Line Statements

Every `if`, `else`, `foreach`, `for`, `while`, and `do` block **must** use `{ }` braces, with the body on its own line(s). No exceptions, even for trivial one-liners.

**Not acceptable:**

```csharp
if (x == null) return;
if (x == null) { return; }
foreach (var item in list) DoSomething(item);
```

**Required:**

```csharp
if (x == null)
{
    return;
}

foreach (var item in list)
{
    DoSomething(item);
}
```

---

## 2. XML Doc Comments — Required on Every Member

Every class, interface, method, constructor, and property **must** have a complete XML doc comment block. This applies to `public`, `protected`, `private`, `internal`, static, abstract, and override members alike — with no exceptions.

**Required elements:**

- `/// <summary>` — on everything
- `/// <param name="...">` — one entry per parameter
- `/// <returns>` — on every non-`void` method (omit only for constructors and `void` methods)

**Style rules for doc text:**

- Summary sentences are lowercase unless the first word is a proper noun
- Parameter descriptions begin with `a` or `an` and give the full type path, e.g.:
  `a concrete class that implements the Services.Theo.Interfaces.IFieldService interface`
- Return descriptions always begin with `returns a ...` and give the full generic type path, e.g.:
  `returns a Common.Entities.Result object where T is a Common.Entities.Theo.FieldDto object`

**Example — method:**

```csharp
/// <summary>
/// Get a specific field, as we want to display it somewhere i.e. in the admin
/// </summary>
/// <param name="id">the id of the field</param>
/// <returns>returns a Common.Entities.Result object where T is a Common.Entities.Theo.FieldDto object</returns>
public Result<FieldDto> Get(int id)
{
```

**Example — constructor:**

```csharp
/// <summary>
/// the ingredients for my class are as follows...
/// </summary>
/// <param name="repository">a concrete class that implements the Common.Interfaces.IRepository interface Where T is a Theo.TheoEntities Object</param>
public FieldService(IRepository<TheoEntities> repository)
{
    _repository = repository;
}
```

**Example — class:**

```csharp
/// <summary>
/// A concrete class responsible for crud operations on my Fields table.
/// </summary>
public class FieldService : IFieldService
{
```

---

## 3. Brace Style — Allman (Opening Brace on Its Own Line for Types/Methods; Same Line for Initializers)

Opening braces for **class, interface, method, constructor, property, and control-flow** bodies go on the **same line** as the declaration:

```csharp
public class FieldService : IFieldService
{
    public Result<FieldDto> Get(int id)
    {
        if (id < 1)
        {
            return result.Failure(ErrorCodes.InvalidId, "The Id provided was not valid");
        }
    }
}
```

Object / collection initializers use the same same-line opening brace:

```csharp
var result = new Result<SelectionDto<FieldDto>>
{
    Data = new SelectionDto<FieldDto>()
};
```

---

## 4. Namespace and Using Directives

### Namespace style

Block-scoped namespaces with one blank line after the opening `{`:

```csharp
namespace Services.Theo.Implementations
{
    public class FieldService : IFieldService
    {
```

### Using directives

All `using` statements appear at the top of the file, before the namespace. They are **not** sorted into System-first groups; they appear in the order required (typically alphabetical by full namespace). No `global using` in source files.

```csharp
using Common.Attributes;
using Common.Entities;
using Common.Entities.Theo;
using Common.Enums;
using Common.Extensions;
using Common.Interfaces;
using Services.Theo.Extensions.Models;
using Services.Theo.Interfaces;
using System.Linq.Expressions;
using Theo;

namespace Services.Theo.Implementations
{
```

---

## 5. Regions — Structure and Ordering

Every file uses `#region` / `#endregion` blocks. The region name is repeated on the `#endregion` line. A blank line appears immediately inside both the opening and closing region marker.

### Top-level regions (in this order)

| Region name                   | Contents                                         |
| ----------------------------- | ------------------------------------------------ |
| `Member Variables`            | Private readonly fields                          |
| `Constructors`                | All constructors                                 |
| `Create`                      | Create / add operations                          |
| `Read`                        | Get (single) then Select (paginated / overloads) |
| `Update`                      | Update operations                                |
| `Delete`                      | Delete operations                                |
| `Helpers`                     | Private helper methods                           |
| `Begin`                       | Xanthippus noun entry-point                      |
| `BuildGraph` / `GenerateHtml` | Xanthippus noun private workers                  |
| `Setup`                       | Test Base class `[SetUp]` method                 |

Interfaces use the same CRUD region names. Xanthippus nouns use `Begin` instead of CRUD regions, plus one region per major private method.

### Alphabetical ordering within regions

- **Member Variables**: fields are declared alphabetically by field name (e.g. `_aiBatchService`, `_classService`, `_columnService`, …).
- **Create region** in Managers: overloads are in alphabetical order by DTO type name (e.g. `Create(AiBatchDto)`, `Create(ClassDto)`, `Create(ColumnDto)`, …).
- **Read region** in Services: `Get` (single item by id) comes before `Select` overloads. `Select` overloads progress from most-parameters to least (paginated first, then single-int, then List<int>).

### Method-body sub-regions

Each method body uses inner `#region` blocks for structure. The standard sub-region set is:

```csharp
#region Initialize
// result object creation
#endregion Initialize

#region Bounds Checking
// id < 1 guards, null checks
#endregion Bounds Checking

#region Fetch Data
// repository queries, LINQ filters, ordering
#endregion Fetch Data

#region Process
// mapping, calculation, result population
#endregion Process
```

Not every method needs every sub-region. Use only those that are relevant. `Initialize` is almost always present; `Bounds Checking` is present when there are guard clauses.
