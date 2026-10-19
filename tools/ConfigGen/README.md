# 配置编译工具

ConfigGen 是无第三方依赖的 .NET 8 命令行工具。它验证配置源并生成普通 C# DTO、枚举和带 schema hash 的 JSON 表。产物可由 Unity、Godot、无图形宿主使用，工具自身无需进入引擎工程。

```powershell
dotnet run --project tools/ConfigGen -- schema.json Data Generated
dotnet run --project tools/ConfigGen -- schema.json Data Generated --check
```

数据目录是平面目录；每张表必须恰好有一个 `<Table>.json` 或 `<Table>.csv`，可以在不同表间混用。重复来源、未知表、缺失表会失败。`--check` 检查全部预期产物的内容，并拒绝残留的 `.cs` / `.json` 旧产物；生成时也要求显式清理旧产物。协议生成仍使用原有 `--protocol` 入口。

## Schema

```json
{
  "namespace": "Example.Configuration",
  "enums": [{ "name": "Rarity", "values": { "Common": 0, "Rare": 1 } }],
  "types": [{ "name": "Reward", "fields": [
    { "name": "ItemId", "type": "int", "references": "Item" },
    { "name": "Count", "type": "int" }
  ] }],
  "tables": [{ "name": "Item", "fields": [
    { "name": "Id", "type": "int" },
    { "name": "Title", "type": "string" },
    { "name": "Rarity", "type": "Rarity" },
    { "name": "Tags", "type": "string[]" },
    { "name": "Rewards", "type": "Reward[]" }
  ] }]
}
```

- 标量：`int`、`long`、`float`、`double`、`bool`、`string`；数字必须在目标类型范围内，浮点值必须有限。
- `enums` 显式声明枚举名称和唯一的 int 值。数据接受精确匹配的枚举名称字符串或已声明的数字值。
- `types` 声明可复用的嵌套结构。字段使用结构名；结构之间可以嵌套，但不允许循环引用。表名不能作为嵌套字段类型。
- 列表使用 `T[]`，支持标量、枚举、结构和多层数组（如 `int[][]`）。空数组有效。
- 每个字段都必须存在，任何层级的 `null` 均无效，包括结构、数组及数组元素；空字符串有效。没有隐式默认值或可空字段。
- 每张表必须有唯一的 int `Id`。int 字段及 int 数组可用 `references` 指向表；编译器递归检查结构和数组内的外键。只有显式 `allowZero: true` 才将 0 视为可选引用。
- 命名空间各段、类型、字段及枚举成员使用 PascalCase C# 标识符。生成类型名在不区分大小写时也必须唯一；重复 JSON 属性、未知/重复数据字段和未声明类型均无效。

每个枚举/结构生成独立 `.cs` 文件，每张表另生成 `.cs` 与 `.json`。DTO 的结构和数组类型在生成时确定，不使用反射构造类型或动态代码生成。实际加载仍由调用方选定的 `ISerializer` 负责；IL2CPP 等 AOT 环境应按该序列化器要求保留 DTO 与泛型实例。

旧版仅含标量的 schema 保留原有 schema hash 和生成格式；显式空 `enums` / `types` 不改变结果。新增定义参与 hash，修改结构或枚举后需重新生成并一起发布 C# 与 JSON。表行按 Id 排序，对象属性按名称排序；字段/定义数组的声明顺序保留。数值及字符串保留 JSON 源中的字面量表示，跨来源逐字比较时应采用一致的数字写法和 JSON 转义方式。

## CSV

CSV/JSON 数据文件均为严格 UTF-8，可带 UTF-8 BOM。CSV 表头必须包含所有字段且不重复，列顺序可调；每条记录列数必须一致。支持逗号、双引号转义和引号内的跨行文本：

```csv
Id,Title,Rarity,Tags,Rewards
1,"Sword, ""starter""",Rare,"[""weapon"",""shop""]","[{""ItemId"":1,""Count"":2}]"
```

string 列直接使用单元格文本；枚举列填写名称或数字；bool 接受 `true` / `false`，大小写均可。其余标量填写与区域设置无关的 JSON 数字。数组和结构单元格填写 JSON 字面量，再按 CSV 规则转义内部双引号。空数值/结构/数组单元格无效；空列表应填写 `[]`。引号结束后的空格也会被视为格式错误。

无需 Excel 库；Excel/表格软件请导出 UTF-8 CSV。示例包含相同内容的 JSON 和 CSV 来源：

```powershell
dotnet run --project tools/ConfigGen -- tools/ConfigGen/Fixtures/Complex/schema.json tools/ConfigGen/Fixtures/Complex/DataCsv artifacts/ConfigExample
dotnet run --project tools/ConfigGen -- tools/ConfigGen/Fixtures/Complex/schema.json tools/ConfigGen/Fixtures/Complex/DataJson artifacts/ConfigExample --check
```

工具不生成运行时序列化器、Excel 导入器或配置热更新传输层；它输出已验证的静态配置产物。CI 应先运行生成检查，再通过资源/内容更新流程发布。
