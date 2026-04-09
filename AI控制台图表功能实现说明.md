# AI控制台图表功能实现说明

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
- 默认选项：**当前使用的模型**（自动识别）
- 可选择特定模型，图表只显示该模型的数据
- 可选择"全部模型"查看所有模型的汇总数据

### 4. 柱状图数值显示
- **柱子顶部显示数值**：
  - Token流量：显示Token数（≥1000时显示为"1.5K"格式）
  - 费用分析：显示费用金额（≥1元显示2位小数，<1元显示4位小数）
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
│  模型过滤：[deepseek-v3 ▼]  ← 默认选中当前模型          │
│                                                         │
│  100 ─────────────────────────────────                 │
│   80 ─────────────────────────────────                 │
│   60 ─────────────────────────────────                 │
│   40 ─────────────────────────────────                 │
│   20 ─────────────────────────────────                 │
│    0 ─────────────────────────────────                 │
│      │ 1.2K  850   2.3K  1.5K  ← 柱上数值               │
│      │  █     █     █     █                            │
│      │  █     █     █     █                            │
│      │  █     █     █     █                            │
│      │  0时   1时   2时   3时   ← X轴标签               │
│                                                         │
│  * 悬停查看详细数据                                      │
└─────────────────────────────────────────────────────────┘
```

---

## 三、实现的文件修改

### 1. 数据层 (`ClawDbContext.cs`)
新增方法：
- `GetUsedModelsAsync()` - 获取所有使用过的模型列表
- `GetTokenFlowAsync(period, startDate, modelFilter)` - 按时间维度聚合Token/费用数据

新增数据模型：
```csharp
public record ChartDataItem
{
    public int TimeUnit { get; init; }      // 小时/天/月
    public string Model { get; init; }
    public int TotalTokens { get; init; }
    public decimal TotalCost { get; init; }
}
```

### 2. ViewModel层 (`ConsoleViewModel.cs`)
新增属性：
- `SelectedTabIndex` - Tab索引（0=流量，1=费用）
- `SelectedPeriod` - 时间维度（today/week/month/year）
- `SelectedModelFilter` - 模型过滤器（默认选中当前模型）
- `AvailableModels` - 可用模型列表
- `ChartData` - 图表数据点集合
- `ChartTitle` - 图表标题（动态）
- `YAxisLabel` - Y轴标签（动态）
- `MaxYValue` - Y轴最大值（自动计算）
- `YAxisTicks` - Y轴刻度值集合

新增命令：
- `ChangePeriodCommand` - 切换时间维度
- `ChangeModelFilterCommand` - 切换模型过滤

新增方法：
- `LoadCurrentModelAsync()` - 加载当前使用的模型ID
- `RefreshChartDataAsync()` - 刷新图表数据
- `GenerateYAxisTicks()` - 生成Y轴刻度
- `UpdateChartLabels()` - 更新图表标题和Y轴标签
- `GetTimeLabel()` - 获取时间标签（小时/星期/日期/月份）
- `GetWeekDayName()` - 获取星期名称（周一~周日）

ChartPoint数据模型增强：
```csharp
public class ChartPoint
{
    public int TimeUnit { get; set; }
    public string Label { get; set; }
    public double Value { get; set; }
    public string ValueText { get; set; }  // 新增：柱上显示的数值文本
    public string Model { get; set; }
    public string Tooltip { get; set; }
}
```

### 3. 视图层 (`ConsoleView.xaml`)
新增UI元素：
- Tab切换按钮（流量分析/费用分析）
- 时间维度按钮组（当天/本周/本月/今年）
- 模型过滤下拉框（默认选中当前模型）
- Y轴刻度显示区域（左侧50px宽）
- 柱状图数值显示（柱子顶部）
- 动态柱状图（支持悬停提示）

布局改进：
- 使用Grid两列布局：左侧Y轴刻度，右侧图表
- 柱子宽度增加到28px，便于显示数值
- 图表高度200px，柱子最大高度150px

### 4. 转换器 (`Converters.cs`)
新增转换器：
- `TabBgConverter` - Tab按钮激活状态背景色
- `ChartHeightConverter` - 计算柱状图高度（值/最大值 × 150px）
- `YAxisPositionConverter` - 计算Y轴刻度位置（从底部向上）

### 5. 事件处理 (`ConsoleView.xaml.cs`)
新增事件：
- `FlowTab_Click` - 切换到流量分析Tab
- `CostTab_Click` - 切换到费用分析Tab

---

## 四、使用说明

### 操作流程
1. **切换Tab**：点击"📊 流量分析"或"💰 费用分析"
2. **选择时间维度**：点击"当天"/"本周"/"本月"/"今年"按钮
3. **过滤模型**：从下拉框选择特定模型（默认已选中当前使用的模型）或"全部模型"
4. **查看详情**：鼠标悬停在柱状图上查看具体数值

### 数据示例
**流量分析 - 当天**
- 横轴：0时、1时、2时...23时
- 纵轴：Token数（自动刻度）
- 柱上数值：`1.2K`（表示1200 Tokens）
- 悬停提示：`15时: 12,450 Tokens (deepseek-v3)`

**费用分析 - 本周**
- 横轴：周一、周二...周日
- 纵轴：费用（元）
- 柱上数值：`¥2.35`
- 悬停提示：`周三: ¥2.3456 (qwen-max)`

---

## 五、技术亮点

### 1. 智能默认选择
- 自动识别当前使用的模型
- 模型过滤器默认选中当前模型
- 用户无需手动选择即可查看当前模型的数据

### 2. 数值格式化
- Token数：≥1000时显示为"1.5K"格式，节省空间
- 费用：根据金额大小自动选择小数位数
  - ≥1元：显示2位小数（¥2.35）
  - <1元：显示4位小数（¥0.0042）

### 3. Y轴刻度系统
- 自动生成5条均匀分布的刻度线
- 刻度值根据最大值动态计算
- 刻度线延伸到图表区域，便于对比数值

### 4. 响应式设计
- 属性变化自动触发图表刷新
- 数据绑定实时更新UI
- 异步查询不阻塞UI线程

### 5. 智能聚合
- 根据时间维度自动选择聚合粒度（小时/天/月）
- 支持模型维度的分组统计
- 数据库层聚合，性能优异

---

## 六、数据库查询示例

### 当天按小时聚合Token流量（过滤特定模型）
```csharp
var data = await db.ApiUsageLogs
    .Where(l => l.Status == "success" 
        && l.Timestamp >= DateTime.Today
        && l.Model == "deepseek-v3")
    .GroupBy(l => new { l.Timestamp.Hour, l.Model })
    .Select(g => new ChartDataItem
    {
        TimeUnit = g.Key.Hour,
        Model = g.Key.Model,
        TotalTokens = g.Sum(x => x.TokensTotal),
        TotalCost = g.Sum(x => x.CostCny)
    })
    .ToListAsync();
```

---

## 七、优化细节

### 已实现的优化
✅ **柱上数值显示** - 直观查看每个时间点的数据  
✅ **Y轴刻度线** - 便于对比不同柱子的高度  
✅ **默认选中当前模型** - 减少用户操作步骤  
✅ **智能数值格式化** - 根据数值大小自动选择最佳显示格式  
✅ **悬停详细提示** - 显示完整的数值和模型信息  

### 未来可增强功能
1. **导出功能**：导出图表数据为CSV/Excel
2. **对比功能**：同时显示多个模型的对比柱状图（堆叠或并列）
3. **趋势线**：添加移动平均线显示趋势
4. **预测功能**：基于历史数据预测未来费用
5. **告警设置**：在图表上标注预警阈值线
6. **自定义时间范围**：支持用户选择任意日期范围

---

## 八、编译状态

✅ **编译成功** - 0个警告，0个错误

项目已成功编译，所有功能已集成到Release版本。

---

**实现日期**：2026年4月9日  
**版本**：ClawBY19 V2.3  
**状态**：✅ 已完成并通过编译  
**更新**：✅ 已添加柱上数值显示、Y轴刻度、默认选中当前模型

---

## 二、实现的文件修改

### 1. 数据层 (`ClawDbContext.cs`)
新增方法：
- `GetUsedModelsAsync()` - 获取所有使用过的模型列表
- `GetTokenFlowAsync(period, startDate, modelFilter)` - 按时间维度聚合Token/费用数据

新增数据模型：
```csharp
public record ChartDataItem
{
    public int TimeUnit { get; init; }      // 小时/天/月
    public string Model { get; init; }
    public int TotalTokens { get; init; }
    public decimal TotalCost { get; init; }
}
```

### 2. ViewModel层 (`ConsoleViewModel.cs`)
新增属性：
- `SelectedTabIndex` - Tab索引（0=流量，1=费用）
- `SelectedPeriod` - 时间维度（today/week/month/year）
- `SelectedModelFilter` - 模型过滤器
- `AvailableModels` - 可用模型列表
- `ChartData` - 图表数据点集合
- `ChartTitle` - 图表标题（动态）
- `YAxisLabel` - Y轴标签（动态）
- `MaxYValue` - Y轴最大值（自动计算）

新增命令：
- `ChangePeriodCommand` - 切换时间维度
- `ChangeModelFilterCommand` - 切换模型过滤

新增方法：
- `RefreshChartDataAsync()` - 刷新图表数据
- `UpdateChartLabels()` - 更新图表标题和Y轴标签
- `GetTimeLabel()` - 获取时间标签（小时/星期/日期/月份）
- `GetWeekDayName()` - 获取星期名称（周一~周日）

### 3. 视图层 (`ConsoleView.xaml`)
新增UI元素：
- Tab切换按钮（流量分析/费用分析）
- 时间维度按钮组（当天/本周/本月/今年）
- 模型过滤下拉框
- 动态柱状图（支持悬停提示）
- Y轴标签显示

### 4. 转换器 (`Converters.cs`)
新增转换器：
- `TabBgConverter` - Tab按钮激活状态背景色
- `ChartHeightConverter` - 计算柱状图高度（值/最大值 × 最大高度）

### 5. 事件处理 (`ConsoleView.xaml.cs`)
新增事件：
- `FlowTab_Click` - 切换到流量分析Tab
- `CostTab_Click` - 切换到费用分析Tab

---

## 三、使用说明

### 界面布局
```
┌─────────────────────────────────────────────────────────┐
│  📊 AI 控制台                                            │
├─────────────────────────────────────────────────────────┤
│  [今日统计卡片] [平均响应] [本月消耗] [累计消耗]          │
├─────────────────────────────────────────────────────────┤
│  [📊 流量分析] [💰 费用分析]  ← Tab切换                  │
│                                                         │
│  当天Token使用量  [当天] [本周] [本月] [今年]  ← 时间维度 │
│                                                         │
│  模型过滤：[全部模型 ▼]  ← 模型过滤器                    │
│                                                         │
│  Token数 ↑                                              │
│  ┌─────────────────────────────────────────────────┐   │
│  │     █                                           │   │
│  │  █  █  █                                        │   │
│  │  █  █  █  █                                     │   │
│  │  0  1  2  3  4  5  6  7  8  9  10 ... 22 23    │   │
│  │                    小时                          │   │
│  └─────────────────────────────────────────────────┘   │
│  * 悬停查看详细数据                                      │
├─────────────────────────────────────────────────────────┤
│  [最近调用记录表格]                                      │
└─────────────────────────────────────────────────────────┘
```

### 操作流程
1. **切换Tab**：点击"📊 流量分析"或"💰 费用分析"
2. **选择时间维度**：点击"当天"/"本周"/"本月"/"今年"按钮
3. **过滤模型**：从下拉框选择特定模型或"全部模型"
4. **查看详情**：鼠标悬停在柱状图上查看具体数值

### 数据示例
**流量分析 - 当天**
- 横轴：0时、1时、2时...23时
- 纵轴：Token数
- 悬停提示：`15时: 12,450 Tokens (deepseek-v3)`

**费用分析 - 本周**
- 横轴：周一、周二...周日
- 纵轴：费用（元）
- 悬停提示：`周三: ¥2.3456 (qwen-max)`

---

## 四、技术亮点

### 1. 响应式设计
- 属性变化自动触发图表刷新（`OnSelectedPeriodChanged`）
- 数据绑定实时更新UI

### 2. 智能聚合
- 根据时间维度自动选择聚合粒度（小时/天/月）
- 支持模型维度的分组统计

### 3. 动态适配
- Y轴最大值自动计算（最大值 × 1.2）
- 费用单位根据金额大小自动切换
- 图表标题根据Tab和时间维度动态生成

### 4. 性能优化
- 使用EF Core的GroupBy在数据库层聚合
- 避免大量数据传输到应用层
- 异步查询不阻塞UI线程

---

## 五、数据库查询示例

### 当天按小时聚合Token流量
```csharp
var data = await db.ApiUsageLogs
    .Where(l => l.Status == "success" && l.Timestamp >= DateTime.Today)
    .GroupBy(l => new { l.Timestamp.Hour, l.Model })
    .Select(g => new ChartDataItem
    {
        TimeUnit = g.Key.Hour,
        Model = g.Key.Model,
        TotalTokens = g.Sum(x => x.TokensTotal),
        TotalCost = g.Sum(x => x.CostCny)
    })
    .ToListAsync();
```

### 本月按天聚合费用
```csharp
var startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
var data = await db.ApiUsageLogs
    .Where(l => l.Status == "success" && l.Timestamp >= startDate)
    .GroupBy(l => new { Day = l.Timestamp.Day, l.Model })
    .Select(g => new ChartDataItem
    {
        TimeUnit = g.Key.Day,
        Model = g.Key.Model,
        TotalTokens = g.Sum(x => x.TokensTotal),
        TotalCost = g.Sum(x => x.CostCny)
    })
    .ToListAsync();
```

---

## 六、扩展建议

### 未来可增强功能
1. **导出功能**：导出图表数据为CSV/Excel
2. **对比功能**：同时显示多个模型的对比柱状图（堆叠或并列）
3. **趋势线**：添加移动平均线显示趋势
4. **预测功能**：基于历史数据预测未来费用
5. **告警设置**：在图表上标注预警阈值线
6. **自定义时间范围**：支持用户选择任意日期范围

### 性能优化方向
1. **缓存机制**：缓存最近查询的图表数据
2. **增量更新**：只更新变化的数据点
3. **虚拟化**：大数据量时使用虚拟化渲染
4. **后台预加载**：提前加载常用时间维度的数据

---

## 七、测试建议

### 功能测试
- [ ] Tab切换正常，数据正确刷新
- [ ] 4个时间维度数据正确显示
- [ ] 模型过滤器正常工作
- [ ] 悬停提示显示正确信息
- [ ] Y轴单位动态切换正确

### 边界测试
- [ ] 无数据时显示空图表
- [ ] 单个数据点正常显示
- [ ] 大量数据点（>100）性能正常
- [ ] 模型名称过长时正确截断
- [ ] 费用为0时正确显示

### 兼容性测试
- [ ] 不同主题下颜色正常
- [ ] 窗口缩放时布局正常
- [ ] 数据库无记录时不报错

---

## 八、编译状态

✅ **编译成功** - 0个警告，0个错误

项目已成功编译，所有功能已集成到Release版本。

---

**实现日期**：2026年4月9日  
**版本**：ClawBY19 V2.3  
**状态**：✅ 已完成并通过编译
