# HTTP 协议契约示例

`schema.json` 定义 endpoint、HTTP 方法/相对路径及请求响应字段；`baseline.json` 是兼容性审查基线。执行 `tools/generate-protocol.ps1` 生成 `Generated/GameProtocol.cs`，包含 DataContract DTO 和 Networking 的强类型 endpoint。

`-Check` 同时检查生成代码与保守的线格式兼容规则：现有 endpoint 和字段不能删除/改类型/改必填性；新增字段需可选。修改基线属于明确的协议升级决策，不能通过自动覆盖基线消除错误。

生成类型在 .NET 测试中真实编译并通过运行时 codec 验证。该示例不连接服务器，不生成业务登录/错误提示；客户端传输与会话边界在可选 Networking 包中。当前生成器覆盖固定路径 HTTP 与标量字段，复杂对象/集合、GET query绑定和跨语言产物需扩展 schema 后另行验证。
