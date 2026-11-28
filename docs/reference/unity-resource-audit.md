# Unity 资源与图集审计

Unity 的序列化引用检查覆盖场景和 Prefab。程序或配置按字符串加载的资源由项目提供审计清单。清单放在游戏工作区，审计在隔离工程中使用真实 Unity 执行。

## Resources 键

```json
{
  "schemaVersion": 1,
  "resources": [
    {
      "key": "UI/Panel",
      "type": "UnityEngine.GameObject",
      "origin": "configuration/ui.csv id=panel prefab",
      "fallbackKeys": ["UI/DefaultPanel"]
    }
  ]
}
```

用 `tools/verify-game-unity.ps1 -Name <Game> -EditorPath '<Unity.exe>' -ResourceSpec '<spec.json>'` 运行。报告记录每项的来源、解析结果、匹配数、资源路径和实际类型。主键缺失时按清单顺序查找回退键，类型错误和重复资源仍会失败。可用 `optional: true` 声明可缺省资源，用 `allowMultiple: true` 声明允许返回多个对象的目录或子资源查询。同键对应多个资源文件仍会报错。

## Sprite Atlas

```json
{
  "schemaVersion": 1,
  "groups": [
    { "atlas": "Assets/UI/Menu.spriteatlasv2", "sources": ["Assets/UI/Play.png", "Assets/UI/Exit.png"] }
  ]
}
```

用同一命令的 `-AtlasSpec '<atlas-spec.json>'` 检查每张图集的 Sprite 数量和成员能否绑定。项目维护实际分组和图集生成配置。Player 中的绘制提交与纹理绑定可以通过后台冒烟留下证据。 [Player 冒烟](unity-player-smoke.md)

`summary.json` 的 `resources` 和 `atlases` 字段记录结果和详细报告路径。项目迁入前可单独检查源工程和目标工程的版本、依赖与序列化引用。 [迁移预检](unity-migration-audit.md)
