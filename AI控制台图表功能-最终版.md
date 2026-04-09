# AI控制台图表功能实现说明（最终版）

## 实现概述

已成功为ClawBY19的AI控制台添加了**流量分析**和**费用分析**两个实时图表功能，支持Tab切换、多时间维度和模型过滤。

---

## 一、功能特性

### 1. Tab切换
- **📊 流量分析**：显示Token使用量统计
- **💰 费用分析**：显示Token流量产生的费用

### 2. 时间维度（4个维度）
| 维度 | 横轴单位 | 数据范围 |
|------|---------|---------|
| **当天** | 小时（0-23时） | 今日0:00至当前 |
| **本周** | 星期（周一~周日） | 本周一至当前 |
| **本月** | 日期（1-31日） | 本月1日至当前 |
| **今年** | 月份（1-12月） | 今年1月至当前 |

### 3. 模型过滤器
- 下拉选择框，显示所有使用过的模型
- **默认选项：当前程序选用的模型**（从models.toml自动读取）
- 可选择特定模型，图表只显示该模型的数据
- 可选择"全部模型"查看所有模型的汇总数据

### 4. 柱状图数值显示
- **柱子顶部显示数值**：
  - **Token流量**：显示为"1234个"格式（不使用K简写）
  - **费用分析**：显示费用金额（≥1元显示2位小数，<1元显示4位小数）
- **悬停提示**：鼠标悬停显示完整详细信息

### 5. Y轴刻度线
- 自动生成5条刻度线（0%, 20%, 40%, 60%, 80%, 100%）
- 刻度值显示在左侧
- 刻度线延伸到图表区域，便于对比

### 6. 动态Y轴单位（费用分析）
根据费用金额自动调整单位，便于查看：
- **≤ 1元**：以**分**为单位
- **1-10元**：以**元**为单位
- **≥ 10元**：以**千元**为单位

---

## 二、界面效果

### 图表布局
```
┌─────────────────────────────────────────────────────────┐
│  [📊 流量分析] [💰 费用分析]  ← Tab切换                  │
│                                                         │
│  当天Token使用量  [当天] [本周] [本月] [今年]  ← 时间维度 │
│                                                         │
│  模型过滤：[deepseek-v3 ▼]  ← 默认选中当前程序使用的模型 │
│                                                         │
│  100 ─────────────────────────────────                 │
│   80 ─────────────────────────────────                 │
│   60 ─────────────────────────────────                 │
│   40 ─────────────────────────────────                 │
│   20 ─────────────────────────────────                 │
│    0 ─────────────────────────────────                 │
│      │ 1234个 850个 2345个 1500个 ← 柱上数值（个）      │
│      │  █     █     █     █                            │
│      │  █     █     █     █                            │
│      │  █     █     █     █                            │
│      │  0时   1时   2时   3时   ← X轴标签               │
│                                                         │
│  * 悬停查看详细数据                                      │
└─────────────────────────────────────────────────────────┘
```

---

## 三、核心实现

### 1. Token数值显示格式
```csharp
// Token显示为"个"，不使用K简写
var valueText = isTokenFlow
    ? $"{item.TotalTokens}个"  // 例如：1234个
    : item.TotalCost >= 1 ? $"¥{item.TotalCost:F2}" : $"¥{item.TotalCost:F4}";
```

### 2. 当前模型自动识别
```csharp
private async Task LoadCurrentModelAsync()
{
    // 从ConfigService获取当前选用的模型
    if (_configService != null)
    {
        var modelCfg = _configService.ModelCfg;
        if (modelCfg.ModelSelection.Mode == "user_specified")
        {
            _currentModelId = modelCfg.ModelSelection.SpecifiedModel;
        }
        else
        {
            _currentModelId = modelCfg.ClawAuto.DefaultModel;
        }
    }
}
```

### 3. 模型过滤器默认选择
```csharp
private async Task LoadAvailableModelsAsync()
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    var models = await db.GetUsedModelsAsync();

    AvailableModels.Clear();
    AvailableModels.Add("全部模型");
    foreach (var m in models)
        AvailableModels.Add(m);

    // 设置默认选中当前程序使用的模型
    if (!string.IsNullOrEmpty(_currentModelId) && models.Contains(_currentModelId))
        SelectedModelFilter = _currentModelId;
    else
        SelectedModelFilter = "全部模型";
}
```

---

## 四、修改的文件清单

### 1. 数据层
- **ClawDbContext.cs**
  - 新增 `GetUsedModelsAsync()` - 获取所有使用过的模型
  - 新增 `GetTokenFlowAsync()` - 按时间维度聚合数据
  - 新增 `ChartDataItem` 数据模型

### 2. ViewModel层
- **ConsoleViewModel.cs**
  - 新增 `_configService` 依赖注入
  - 新增 `YAxisTicks` 集合（Y轴刻度）
  - 新增 `_currentModelId` 字段（当前模型ID）
  - 修改 `LoadCurrentModelAsync()` - 从ConfigService读取当前模型
  - 修改 `LoadAvailableModelsAsync()` - 默认选中当前模型
  - 修改 `RefreshChartDataAsync()` - Token显示为"个"
  - 新增 `GenerateYAxisTicks()` - 生成Y轴刻度
  - 增强 `ChartPoint` 模型，添加 `ValueText` 字段

### 3. 视图层
- **ConsoleView.xaml**
  - 添加Y轴刻度显示区域（左侧50px）
  - 添加柱上数值显示（TextBlock）
  - 调整柱子宽度为28px
  - 使用Grid两列布局

### 4. 转换器
- **Converters.cs**
  - 新增 `TabBgConverter` - Tab激活状态
  - 新增 `ChartHeightConverter` - 柱高度计算
  - 新增 `YAxisPositionConverter` - Y轴刻度位置

### 5. 应用配置
- **App.xaml**
  - 注册 `YAxisPositionConverter` 转换器

---

## 五、使用说明

### 操作流程
1. **切换Tab**：点击"📊 流量分析"或"💰 费用分析"
2. **选择时间维度**：点击"当天"/"本周"/"本月"/"今年"按钮
3. **过滤模型**：
   - 默认已选中当前程序使用的模型
   - 可切换到"全部模型"查看所有模型数据
   - 可选择其他特定模型
4. **查看详情**：鼠标悬停在柱状图上查看完整数值

### 数据示例

**流量分析 - 当天（默认选中deepseek-v3）**
- 横轴：0时、1时、2时...23时
- 纵轴：Token数（自动刻度）
- 柱上数值：`1234个`
- 悬停提示：`15时: 12,450 Tokens (deepseek-v3)`

**费用分析 - 本周（全部模型）**
- 横轴：周一、周二...周日
- 纵轴：费用（元）
- 柱上数值：`¥2.35`
- 悬停提示：`周三: ¥2.3456 (qwen-max)`

---

## 六、技术亮点

### 1. 智能默认选择
✅ 自动从models.toml读取当前程序选用的模型  
✅ 支持两种模式：user_specified（指定模型）和claw_auto（自动选择）  
✅ 模型过滤器默认选中当前模型，无需手动选择  

### 2. 清晰的数值显示
✅ Token数量显示为"个"，不使用K简写，避免混淆  
✅ 柱子顶部直接显示数值，一目了然  
✅ 费用根据金额大小智能选择小数位数  

### 3. Y轴刻度系统
✅ 自动生成5条均匀分布的刻度线  
✅ 刻度值根据最大值动态计算  
✅ 刻度线延伸到图表区域，便于对比  

### 4. 响应式设计
✅ 属性变化自动触发图表刷新  
✅ 数据绑定实时更新UI  
✅ 异步查询不阻塞UI线程  

---

## 七、配置文件示例

### models.toml
```toml
[model_selection]
mode = "user_specified"  # 或 "claw_auto"
specified_model = "deepseek-v3"  # 当前选用的模型

[claw_auto]
default_model = "deepseek-v3"  # 自动模式的默认模型
```

程序会自动读取：
- 如果 `mode = "user_specified"`，使用 `specified_model`
- 如果 `mode = "claw_auto"`，使用 `default_model`

---

## 八、编译状态

✅ **编译成功** - 0个警告，0个错误

所有功能已集成并可以直接运行使用！

---

## 九、功能对比

| 功能 | 初版 | 最终版 |
|------|------|--------|
| Token数值显示 | 使用K简写（1.2K） | 显示完整数字+个（1234个） |
| 模型过滤默认值 | "全部模型" | 当前程序选用的模型 |
| Y轴刻度 | ❌ 无 | ✅ 5条刻度线 |
| 柱上数值 | ❌ 无 | ✅ 显示在柱子顶部 |
| 模型识别 | 手动配置 | 自动从ConfigService读取 |

---

**实现日期**：2026年4月9日  
**版本**：ClawBY19 V2.3 Final  
**状态**：✅ 已完成并通过编译  
**最终优化**：
- ✅ Token显示为"个"（不使用K）
- ✅ 模型过滤默认选中当前程序使用的模型
- ✅ Y轴刻度线完整显示
- ✅ 柱上数值清晰可见
