# CatClawMusic MAUI 迁移 - 已知问题及修复

## 问题 1: "Cannot determine property to provide the value for."

### 错误信息
```
System.InvalidOperationException: Cannot determine property to provide the value for.
```

### 原因
`CommunityToolkit.Mvvm` 的 `[ObservableProperty]` 属性必须应用在**字段**上，而不是**属性**上。

**错误用法**：
```csharp
[ObservableProperty]
public partial string Title { get; set; } = "";
```

**正确用法**：
```csharp
[ObservableProperty]
private string _title = "";
```

### 已修复的文件
- ✅ `ViewModels/AppViewModels.cs` (NowPlayingViewModel)

### 检查清单
运行以下命令检查是否还有错误的用法：

```bash
cd "D:/Code/CatClawMusic/CatClawMusic.Maui/ViewModels"
grep -B 1 "public partial" *.cs | grep -B 1 "ObservableProperty"
```

### 如果错误仍然存在
1. 清理并重新构建：
   ```bash
   dotnet clean CatClawMusic.Maui/CatClawMusic.Maui.csproj
   dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj
   ```

2. 检查 XAML 文件中的数据绑定是否正确

3. 在 Android Device Log 中查找完整的堆栈跟踪，确定是哪个 ViewModel/属性导致的错误

## 问题 2: 构建警告（非阻塞）

### 警告列表
- **CS0618**: 过时 API 使用（Frame → Border、DisplayAlert → DisplayAlertAsync）
- **CS0067**: 未使用的事件
- **CS0169**: 未使用的字段

### 修复建议
在后续任务中逐步修复，不影响当前功能测试。

## 下一步

请重新部署应用到 Android 设备，测试是否还会出现错误。
