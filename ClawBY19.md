# ClawBY19 —— 自学习型本地AI智能体平台 · 系统设计方案

> **版本**：V2.0  
> **日期**：2026年4月  
> **代号**：ClawBY19（小龙虾BY19）  
> **核心理念**：轻量极速 · 自我进化 · 费用可控  
> **部署环境**：Windows 10/11 单机部署，双击安装即用  
> **开发语言**：C# · .NET 9  
> **数据库选型**：SQLite（Microsoft.Data.Sqlite + EF Core + sqlite-vec向量扩展）

---

## 一、设计总纲

### 1.1 与OpenClaw的关系

ClawBY19参照OpenClaw全部核心功能（文件系统操作、浏览器自动化、代码执行、MCP协议、Skills技能扩展、多渠道消息等），在此基础上重点突破三个方向：

| 维度 | OpenClaw | ClawBY19 差异化 |
|------|----------|----------------|
| 体积与速度 | 依赖Node.js全量安装，体积较大 | C# .NET 9 单文件 + WPF，安装包<100MB |
| 学习能力 | 无内置学习机制，每次从零开始 | 自带RAG学习笔记库，越用越聪明，不重复犯错 |
| 费用管控 | 无费用统计 | AI控制台实时追踪Token/费用，预警闪灯机制 |

### 1.2 五大核心设计要求

| 编号 | 要求 | 设计章节 |
|------|------|---------|
| R1 | 极速启动、最小磁盘占用 | 第三章 |
| R2 | 自带RAG学习笔记库 + 人机交互反馈 + 成功执行总结 | 第四章 |
| R3 | 自我学习升级功能，输出"学习升级总结.md" | 第五章 |
| R4 | AI控制台（模型/Token/费用/图表） | 第六章 |
| R5 | 多级费用预警阈值 + 闪灯提示 | 第七章 |

### 1.3 技术选型总览

| 层次 | 组件 | 选型 | 理由 |
|------|------|------|------|
| 语言/运行时 | 核心引擎 | **C# .NET 9** | 开发效率高，.NET生态成熟，类库丰富，Windows原生集成 |
| GUI | 桌面界面 | **WPF（XAML）** | Windows原生GUI框架，随系统共享运行时，无额外体积 |
| 数据库 | 关系数据+向量 | **SQLite（Microsoft.Data.Sqlite + EF Core）** | 见1.4节 |
| ORM | 数据访问层 | **Entity Framework Core 9** | C#官方ORM，代码优先迁移，LINQ查询 |
| 图表 | 费用可视化 | **LiveCharts2（WPF版）** | 开源，WPF原生，实时动态图表 |
| 浏览器自动化 | 网页操控 | **Microsoft.Playwright** | .NET官方Playwright绑定，跨浏览器 |
| 配置格式 | 设置文件 | **TOML（Tomlyn）+ appsettings.json** | TOML可读性强，appsettings.json符合.NET标准 |
| 日志 | 运行记录 | **Serilog** | 结构化日志，自动文件轮转，.NET生态标准 |
| HTTP | API调用 | **HttpClient + System.Net.Http** | .NET内置，支持连接池和流式输出 |
| 打包 | 部署 | **.NET PublishSingleFile + Inno Setup** | 单EXE分发，可附带.NET 9运行时检测安装 |

### 1.4 数据库选型说明

**选型：SQLite（Microsoft.Data.Sqlite + EF Core 9 + sqlite-vec扩展）**

| 考量维度 | SQLite方案 | 理由 |
|---------|-----------|------|
| 部署方式 | 零部署，单文件内嵌 | 用户无需安装数据库服务，开箱即用 |
| .NET集成 | Microsoft官方维护 | EF Core原生支持，代码优先迁移，LINQ查询 |
| 向量检索 | sqlite-vec扩展 | 支持HNSW索引，RAG语义检索无需额外服务 |
| 全文搜索 | FTS5内置扩展 | 支持中文分词全文检索，无额外依赖 |
| 体积 | 起步<1MB，按需增长 | claw.db随数据增长，无最低空间浪费 |
| 性能 | WAL模式无锁并发 | 10万条记录Top-5向量检索<20ms |
| 备份 | 直接复制.db文件 | 数据库即单文件，用户可随时备份 |

---

## 二、大模型支持矩阵 & Claw模型选择模式

### 2.1 模型支持清单

> **优先级说明**：中国开源模型置于最优先位置。本地Ollama模型完全免费，适合隐私或离线场景。
> 所有模型ID均为系统配置文件中使用的标识符，区分大小写。

---

#### ▶ A类：中国开源模型（最优先，可本地部署或API调用）

| 模型ID | 全称 | 提供商 | 参数量 | 核心特点 | 上下文 | 类型 |
|--------|------|--------|--------|----------|--------|------|
| `deepseek-v3` | DeepSeek-V3 | 深度求索 | 685B MoE | 综合能力强，极高性价比，理解+代码+推理均衡优秀，开源旗舰 | 128K | 开源API |
| `deepseek-r1` | DeepSeek-R1 | 深度求索 | 685B MoE | 强化学习推理专项，数学/逻辑/代码推理标杆，开源推理之王 | 128K | 开源API |
| `deepseek-r1-distill-qwen-32b` | DeepSeek-R1-Distill-Qwen-32B | 深度求索 | 32B Dense | R1蒸馏版，推理能力强，体积适中，本地部署首选 | 128K | 开源 |
| `qwen3-235b` | Qwen3-235B-A22B | 阿里通义 | 235B MoE | 混合思考/直答双模式，超大MoE，多语言，综合性能顶级 | 128K | 开源API |
| `qwen2.5-72b` | Qwen2.5-72B-Instruct | 阿里通义 | 72B Dense | 中文/代码/数学综合最优均衡，开源全能旗舰 | 128K | 开源API |
| `qwq-32b` | QwQ-32B | 阿里通义 | 32B Dense | 深度推理思考链，媲美o3-mini，数学竞赛级 | 32K | 开源API |
| `qwen2.5-coder-32b` | Qwen2.5-Coder-32B-Instruct | 阿里通义 | 32B Dense | 代码专项旗舰，超越GPT-4o代码，支持92种编程语言 | 128K | 开源API |
| `glm-4-9b` | GLM-4-9B-Chat | 智谱AI | 9B Dense | 轻量开源中文模型，工具调用优化，Agent友好 | 128K | 开源 |
| `internlm3-20b` | InternLM3-20B-Instruct | 上海AI实验室 | 20B Dense | 工具调用原生支持，超长上下文，Agent编排最优 | 1M | 开源 |
| `yi-34b` | Yi-34B-Chat | 零一万物 | 34B Dense | 中英双语均衡，长上下文理解强 | 200K | 开源 |
| `baichuan2-13b` | Baichuan2-13B-Chat | 百川智能 | 13B Dense | 中文文化理解深，知识丰富，轻量可本地 | 4K | 开源 |

---

#### ▶ B类：中国商业API模型

| 模型ID | 全称 | 提供商 | 核心特点 | 上下文 | 参考价格(输入/输出·元/百万Token) |
|--------|------|--------|----------|--------|----------------------------------|
| `qwen-max` | Qwen-Max | 阿里云百炼 | 阿里商业旗舰，多轮对话、工具调用成熟，企业级SLA | 128K | ¥20 / ¥60 |
| `qwen-plus` | Qwen-Plus | 阿里云百炼 | 性价比次旗舰，均衡快速 | 128K | ¥4 / ¥12 |
| `qwen-turbo` | Qwen-Turbo | 阿里云百炼 | 极速响应，最低成本 | 128K | ¥0.3 / ¥0.6 |
| `glm-4-plus` | GLM-4-Plus | 智谱AI | 中文深度理解，多模态，函数调用成熟 | 128K | ¥20 / ¥20 |
| `glm-z1-flash` | GLM-Z1-Flash | 智谱AI | 推理增强型，深度思考链，快速输出 | 64K | ¥10 / ¥10 |
| `moonshot-v1-128k` | Kimi (Moonshot-v1-128k) | 月之暗面 | 超长文档理解专项，文件分析、书籍摘要场景最优 | 128K | ¥12 / ¥12 |
| `minimax-text-01` | MiniMax-Text-01 | MiniMax | 100万Token超长上下文，全球最长，适合全书处理 | 1M | ¥1 / ¥8 |
| `spark4.0-ultra` | Spark4.0-Ultra | 科大讯飞 | 语音文本深度融合，教育医疗行业专项优化 | 128K | ¥30 / ¥30 |
| `ernie-4.5` | ERNIE-4.5 | 百度文心 | 中文生态深度整合，搜索增强，内容创作 | 128K | ¥4 / ¥8 |
| `doubao-pro-32k` | Doubao-Pro-32k | 字节豆包 | 极低成本高效响应，日常任务性价比最高 | 32K | ¥0.8 / ¥2 |
| `hunyuan-turbo` | Hunyuan-Turbo | 腾讯混元 | 腾讯生态整合，办公/搜索场景，长上下文 | 256K | ¥14.9 / ¥49 |
| `baichuan4-turbo` | Baichuan4-Turbo | 百川智能 | 中文文化理解最深，文学创作、古文处理 | 32K | ¥15 / ¥15 |

---

#### ▶ C类：美国主流模型

| 模型ID | 全称 | 提供商 | 核心特点 | 上下文 | 参考价格(输入/输出·元/百万Token) |
|--------|------|--------|----------|--------|----------------------------------|
| `claude-sonnet-4-6` | Claude Sonnet 4.6 | Anthropic | 代码/长文本最强，Agent执行能力顶级，安全性高 | 200K | ¥21 / ¥105 |
| `claude-opus-4-6` | Claude Opus 4.6 | Anthropic | Anthropic旗舰，复杂推理、科研写作 | 200K | ¥105 / ¥525 |
| `claude-haiku-4-5` | Claude Haiku 4.5 | Anthropic | 轻量极速版，低延迟简单任务 | 200K | ¥2.1 / ¥10.5 |
| `gpt-4o` | GPT-4o | OpenAI | 多模态旗舰，工具调用最成熟，生态最广 | 128K | ¥17.5 / ¥52.5 |
| `o3` | o3 | OpenAI | 顶级推理，数学/科学竞赛级，博士难题 | 200K | ¥140 / ¥560 |
| `o4-mini` | o4-mini | OpenAI | 高效推理，o3能力70%的价格10%，性价比推理首选 | 200K | ¥7.7 / ¥31 |
| `gemini-2.5-pro` | Gemini 2.5 Pro | Google | 超长上下文+多模态，代码强，视觉理解 | 1M | ¥25 / ¥105 |
| `gemini-2.0-flash` | Gemini 2.0 Flash | Google | 极速轻量，高吞吐，成本低，实时场景 | 1M | ¥1.4 / ¥4.2 |
| `grok-3` | Grok-3 | xAI | 实时互联网数据接入，深度推理，X平台生态 | 128K | ¥35 / ¥105 |
| `mistral-large-3` | Mistral Large 3 | Mistral AI | 欧洲产，多语言优秀，GDPR合规，隐私友好 | 128K | ¥14 / ¥42 |

---

#### ▶ D类：本地Ollama模型（零成本，离线可用）

| 模型ID | 全称 | 最低内存 | 核心特点 | 适用场景 |
|--------|------|---------|----------|----------|
| `ollama/deepseek-r1:8b` | DeepSeek-R1-8B | 8GB | 本地最佳中文推理，思考链可见 | 隐私推理任务 |
| `ollama/deepseek-r1:1.5b` | DeepSeek-R1-1.5B | 2GB | 极轻量推理，低配设备可用 | 资源受限场景 |
| `ollama/qwen2.5:7b` | Qwen2.5-7B-Instruct | 6GB | 本地中文综合最优，日常任务 | 本地中文问答 |
| `ollama/qwen2.5-coder:7b` | Qwen2.5-Coder-7B | 6GB | 本地代码补全/生成专项 | 离线代码辅助 |
| `ollama/qwq:32b` | QwQ-32B-Q4 | 20GB | 本地深度推理，高配专用 | 高配推理 |
| `ollama/glm4:9b` | GLM-4-9B-Chat | 8GB | 本地中文对话，工具调用支持 | 本地中文Agent |
| `ollama/llama3.3:70b` | Llama 3.3-70B-Q4 | 40GB | Meta开源旗舰英文，高配专用 | 英文高质量 |
| `ollama/gemma3:4b` | Gemma3-4B | 6GB | Google轻量本地，英文优秀 | 轻量英文任务 |
| `ollama/phi4-mini` | Phi-4-mini | 4GB | 微软轻量代码，数学能力强 | 低配代码任务 |

---

### 2.2 Claw模型选择模式

ClawBY19提供两种模型选择模式，均可通过后台配置文件切换，无需修改代码：

```
┌─────────────────────────────────────────────────────────┐
│              Claw 模型选择模式（后台可配置）               │
├─────────────────────┬───────────────────────────────────┤
│   {指定模型} 模式    │        {Claw自选} 模式             │
├─────────────────────┼───────────────────────────────────┤
│ 用户在设置中手动指    │ Claw根据任务类型、上下文长度、       │
│ 定一个固定模型ID，    │ 当前预算、网络状态，自动选择最       │
│ 所有任务均使用该模型  │ 优模型，可配置路由规则和降级策略     │
├─────────────────────┼───────────────────────────────────┤
│ 适合：               │ 适合：                             │
│ · 对特定模型有偏好    │ · 不想关心模型细节的用户            │
│ · 预算绑定某平台      │ · 追求每次任务最优效果              │
│ · 企业统一模型管控    │ · 多任务类型混合使用场景            │
└─────────────────────┴───────────────────────────────────┘
```

### 2.3 模型选择配置文件（可编程）

```toml
# config/models.toml — 模型选择模式配置

# =======================================================
# 模式开关：user_specified（指定模型）| claw_auto（Claw自选）
# =======================================================
[model_selection]
mode = "claw_auto"

# {指定模型} 模式下，全局使用的模型ID（支持上方清单全部模型ID）
specified_model = "deepseek-v3"

# 是否允许用户在每次对话时临时切换模型（不影响全局配置）
allow_per_session_override = true

# =======================================================
# {Claw自选} 模式配置
# =======================================================
[claw_auto]
# 无法判断任务类型时的兜底模型
default_model = "deepseek-v3"

# 是否允许Claw在执行前向用户说明选择原因
explain_selection = true

# -------------------------------------------------------
# 任务类型路由规则（任务类型 -> 首选模型ID）
# -------------------------------------------------------
[claw_auto.task_routing]
code_task          = "deepseek-v3"          # 代码生成、调试、重构
reasoning_task     = "deepseek-r1"          # 数学推导、逻辑分析、证明
long_context_task  = "minimax-text-01"      # 超长文档（>32K Token）处理
chinese_task       = "qwen-max"             # 中文写作、古文、文化理解
quick_task         = "doubao-pro-32k"       # 快速问答（预期响应<3秒）
creative_task      = "baichuan4-turbo"      # 文学创作、营销文案、头脑风暴
analysis_task      = "qwen2.5-72b"          # 数据分析、报告、表格处理
agent_task         = "claude-sonnet-4-6"    # 复杂Agent编排、多步工具调用
offline_task       = "ollama/qwen2.5:7b"    # 无网络 / 隐私数据场景

# -------------------------------------------------------
# 降级策略（主模型不可用时的备选链）
# -------------------------------------------------------
[claw_auto.fallback]
# 降级链：按顺序尝试，第一个可用即使用
chain = [
    "deepseek-v3",          # 第一降级：DeepSeek-V3（主模型）
    "qwen-plus",            # 第二降级：Qwen-Plus（次选）
    "doubao-pro-32k",       # 第三降级：Doubao（最便宜备选）
    "ollama/qwen2.5:7b",    # 最终降级：本地Ollama（离线保底）
]

# 连续失败N次后触发降级
fail_threshold = 3

# -------------------------------------------------------
# 预算感知（超出预算时自动切换更便宜的模型）
# -------------------------------------------------------
[claw_auto.budget]
enabled                  = true
max_cost_per_request_cny = 1.0      # 单次请求最高费用上限（元）
prefer_cheapest_capable  = false    # false=优先效果；true=优先最低成本
# 日预算超过80%时，自动切换到cheapest_fallback
daily_budget_alert_model = "doubao-pro-32k"
```

### 2.4 Claw自选路由策略说明

Claw自选时，内部按以下顺序决策：

```
收到用户任务
    │
    ▼
步骤1：任务类型识别（关键词+语义分析）
  ├─ 含"代码/函数/bug/debug/编程" → code_task
  ├─ 含"推理/证明/数学/计算/逻辑" → reasoning_task
  ├─ 上下文Token预估 > 32K        → long_context_task
  ├─ 含"写作/创作/故事/文案"       → creative_task
  ├─ 任务标记为离线/隐私           → offline_task
  └─ 其他                         → default_model
    │
    ▼
步骤2：预算检查
  ├─ 当日已用预算 < 80%  → 使用路由结果模型
  └─ 当日已用预算 >= 80% → 切换到 daily_budget_alert_model
    │
    ▼
步骤3：可用性检测（Ping API端点，<500ms超时）
  ├─ 目标模型可用  → 发起请求
  └─ 目标模型不可用 → 按 fallback.chain 顺序降级
    │
    ▼
步骤4：执行请求，记录"使用模型"到 api_usage_log
```

---

## 三、R1：极速启动 · 最小磁盘占用

### 3.1 技术架构选型

为实现"执行速度快、占用空间最小"，核心引擎采用C# .NET 9编写，以WPF构建桌面界面：

| 组件 | 传统方案（Electron） | ClawBY19方案（C# .NET 9） | 体积对比 |
|------|---------------------|--------------------------|---------|
| 核心引擎 | Node.js（~80MB） | C# .NET 9 ReadyToRun单文件（~60MB含运行时） | 缩减25% |
| GUI框架 | Electron（~200MB） | WPF（随系统.NET 9共享，0MB额外） | 缩减>90% |
| Python运行时 | 完整Python（~100MB） | 嵌入式Python 3.12 Embed（~15MB） | 缩减85% |
| 浏览器引擎 | 内嵌Chromium（~150MB） | Microsoft.Playwright（按需加载，无内嵌浏览器） | 缩减80% |
| 数据库 | 独立SQLite + Redis | Microsoft.Data.Sqlite（EF Core，单文件嵌入式） | 缩减100% |
| **安装包总体积** | **~500MB** | **< 100MB** | **缩减80%** |

### 3.2 系统架构

```
┌─────────────────────────────────────────────────────┐
│        ClawBY19.exe（C# .NET 9 单文件主程序 ~60MB）    │
│  ┌────────────┐  ┌────────────┐  ┌────────────────┐ │
│  │ Agent Core │  │  WPF GUI   │  │ AI控制台引擎   │ │
│  │(C# Async)  │  │(XAML/MVVM) │  │ 费用/Token统计 │ │
│  └────────────┘  └────────────┘  └────────────────┘ │
├─────────────────────────────────────────────────────┤
│  ┌────────────┐  ┌────────────┐  ┌────────────────┐ │
│  │ 文件操作器  │  │ 浏览器操控  │  │ 代码执行沙箱   │ │
│  │(.NET IO /  │  │(Microsoft  │  │(嵌入Python3.12 │ │
│  │ System.IO) │  │ Playwright)│  │  + 子进程)     │ │
│  └────────────┘  └────────────┘  └────────────────┘ │
├─────────────────────────────────────────────────────┤
│  ┌────────────────────────────────────────────────┐ │
│  │  RAG 学习笔记库（SQLite + sqlite-vec 向量索引）   │ │
│  │  EF Core ORM │ 向量检索 │ FTS5全文搜索 │ BLOB存储 │ │
│  └────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────┤
│  ┌────────────┐  ┌────────────┐  ┌────────────────┐ │
│  │ MCP连接器  │  │ Skills管理  │  │ 渠道桥接器     │ │
│  │(C# WS/HTTP)│  │(动态程序集) │  │(飞书/企微/钉钉)│ │
│  └────────────┘  └────────────┘  └────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### 3.3 启动速度优化

| 优化措施 | 效果 |
|---------|------|
| .NET 9 ReadyToRun（R2R）编译，大幅减少JIT预热时间 | 冷启动<2秒 |
| WPF复用系统渲染管道，无需加载独立WebView | GUI渲染<1秒 |
| EF Core预编译查询（Compiled Queries）+ SQLite WAL模式 | 首次查询<10ms |
| HttpClient连接池预建立（Startup注入IHttpClientFactory） | 首次AI响应减少300ms |
| sqlite-vec向量索引常驻内存（Memory-Mapped File） | 知识检索<50ms |
| 懒加载：Playwright/Python仅在任务需要时初始化 | 不用不占内存 |
| C# Channel<T>流式输出（无锁异步管道） | 首Token延迟<500ms |

### 3.4 磁盘空间管理

```
ClawBY19/                          # 总计 < 100MB（安装时）
├── ClawBY19.exe                   # C# .NET 9 单文件主程序 ~60MB
├── python-embed/                  # 嵌入式Python 3.12 ~15MB
├── playwright-lite/               # Microsoft.Playwright（按需下载）~20MB
├── config/
│   ├── appsettings.json           # 主运行时配置 ~2KB
│   ├── models.toml                # 模型选择模式配置 ~3KB（见第二章）
│   ├── model_pricing.toml         # 模型收费标准（可在线更新）~5KB
│   └── alert_rules.toml           # 费用预警规则 ~1KB
├── data/
│   ├── claw.db                    # 主SQLite数据库（EF Core管理）~1MB起
│   └── workspace/                 # 工作文件空间（用户数据）
├── skills/                        # 技能目录 ~5MB
├── logs/                          # Serilog滚动日志（最大50MB自动清理）
└── 学习升级总结.md                  # 自我学习输出文件
```

---

## 四、R2：RAG学习笔记库 · 人机交互反馈 · 成功执行总结

### 4.1 核心理念

每一次任务执行都是一次学习机会。ClawBY19通过"记录→反思→总结→检索"的闭环，确保同类任务不重复犯错，越用越聪明。

### 4.2 RAG学习笔记库数据模型

```sql
-- 学习笔记表：记录每一次任务的完整生命周期
CREATE TABLE learning_notes (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id         TEXT NOT NULL,           -- 任务唯一ID
    created_at      DATETIME DEFAULT NOW,    -- 创建时间
    task_type       TEXT,                    -- 任务类型标签（文件操作/网页抓取/代码生成...）
    user_prompt     TEXT NOT NULL,           -- 用户原始指令
    execution_plan  TEXT,                    -- Agent生成的执行计划
    execution_log   TEXT,                    -- 完整执行过程日志
    final_result    TEXT,                    -- 最终结果
    status          TEXT DEFAULT 'running',  -- running/success/failed/revised
    
    -- 人机交互反馈记录
    user_feedback   TEXT,                    -- 用户的修改意见（JSON数组）
    revision_count  INTEGER DEFAULT 0,       -- 用户修改次数
    
    -- 成功执行总结（仅status=success时生成）
    summary_problem     TEXT,               -- 问题在哪
    summary_solution    TEXT,               -- 如何破解
    summary_key_steps   TEXT,               -- 成功的关键步骤
    summary_avoid_error TEXT,               -- 下次如何避免出错
    
    -- 向量索引（sqlite-vec扩展存储）
    embedding       BLOB,                   -- 任务描述的向量表示（用于语义检索）
    
    -- 模型消耗
    model_used      TEXT,                   -- 使用的模型ID
    tokens_input    INTEGER DEFAULT 0,      -- 输入Token数
    tokens_output   INTEGER DEFAULT 0,      -- 输出Token数
    cost_cny        REAL DEFAULT 0          -- 费用（元）
);

-- 用户修改意见明细表
CREATE TABLE user_revisions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id         TEXT NOT NULL,
    revision_no     INTEGER,                -- 第几次修改
    timestamp       DATETIME DEFAULT NOW,
    user_opinion    TEXT NOT NULL,           -- 用户的修改意见
    before_action   TEXT,                   -- 修改前Agent的做法
    after_action    TEXT,                   -- 修改后Agent的做法
    applied         BOOLEAN DEFAULT TRUE    -- 是否已应用
);
```

### 4.3 人机交互反馈流程

每次Agent执行任务时，采用"执行-确认-修正"的交互循环：

```
用户下达指令
    │
    ▼
Agent制定执行计划 ──→ 展示给用户："我打算这样做，你看行吗？"
    │
    ▼
用户选择：
  ├─ ✅ "执行" ──→ Agent执行 ──→ 展示结果
  ├─ ✏️ "修改" ──→ 用户输入修改意见 ──→ 【记录到RAG库】──→ Agent调整方案 ──→ 重新确认
  └─ ❌ "取消" ──→ 终止任务
    │
    ▼
执行完成后，用户评价：
  ├─ 👍 "满意" ──→ 标记为success ──→ 触发"成功执行总结"生成
  ├─ 👎 "不满意" ──→ 用户说明哪里不对 ──→ 【记录反馈】──→ Agent修正 ──→ 重新评价
  └─ 🔄 "重做" ──→ 从头开始，但这次Agent会先检索RAG库中的相关经验
```

### 4.4 成功执行总结的自动生成

每当任务标记为"success"，系统自动调用大模型生成四维总结：

**Prompt模板（系统内部调用，不展示给用户）**：

```
你是ClawBY19的自我复盘引擎。请根据以下任务的完整执行记录，生成一份结构化复盘总结。

任务指令：{user_prompt}
执行过程：{execution_log}
用户中途修改意见：{user_feedback}
最终结果：{final_result}

请严格按以下四个维度输出：

1. 【问题在哪】这个任务的难点或容易出错的地方是什么？
2. 【如何破解】最终是通过什么方法解决的？
3. 【关键步骤】执行成功的关键步骤是什么？（列出3-5步）
4. 【避错指南】下次执行类似任务，应该注意什么才能避免犯错？

输出格式为JSON：
{
  "problem": "...",
  "solution": "...",
  "key_steps": ["步骤1", "步骤2", ...],
  "avoid_error": "..."
}
```

### 4.5 RAG检索机制

每次接收新任务时，Agent在正式执行前先检索RAG库：

```
新任务 ──→ 文本向量化 ──→ 在RAG库中检索Top-5最相似的历史任务
                              │
                              ▼
                    检查是否有相关的：
                    ├─ 成功经验 ──→ 参考关键步骤和避错指��
                    ├─ 失败记录 ──→ 避免重蹈覆辙
                    └─ 用户修改意见 ──→ 遵循用户的偏好
                              │
                              ▼
                    将检索到的经验注入System Prompt
                    "根据历史经验，执行此类任务时应注意：..."
                              │
                              ▼
                    Agent制定执行计划（已融合历史经验）
```

### 4.6 向量索引方案（轻量化）

不引入ChromaDB等重量级服务，基于SQLite的sqlite-vec扩展实现向量检索：

| 组件 | 方案 | 体积 |
|------|------|------|
| 向量化 | 调用大模型的Embedding API（如Qwen-Embed / DeepSeek-Embed） | 0MB（远程） |
| 离线备选 | 内嵌ONNX Runtime + MiniLM-L6中文模型（C#绑定） | ~30MB |
| 向量存储 | sqlite-vec扩展（HNSW索引，随claw.db存储） | 0MB（集成） |
| 检索速度 | 10万条记录Top-5检索 < 20ms | — |

---

## 五、R3：自我学习升级功能

### 5.1 功能描述

用户在界面上点击**"升级学习"**按钮后，ClawBY19执行以下流程：

```
点击"升级学习"
    │
    ▼
遍历RAG学习笔记库全部记录
    │
    ▼
按任务类型分类汇总
    │
    ▼
对每个类型，调用大模型生成总结：
  ├─ 该类任务的共性问题
  ├─ 最有效的解决策略
  ├─ 用户最常提出的修改意见（代表用户偏好）
  ├─ 高频错误模式和规避方法
  └─ 改进建议（下一步优化方向）
    │
    ▼
汇总所有类型的总结，生成全局升级报告
    │
    ▼
输出到程序目录下：学习升级总结.md
    │
    ▼
同时将关键结论写入"核心经验库"（永久生效的系统级知识）
```

### 5.2 "学习升级总结.md"的输出格式

```markdown
# ClawBY19 学习升级总结

> 生成时间：2026-04-03 15:30:00  
> 统计周期：2026-03-01 ~ 2026-04-03  
> 总任务数：247次 | 成功：218次 | 失败：14次 | 用户修正：15次  
> 成功率：88.3% → 本周期提升了3.2个百分点

---

## 一、各类任务的经验总结

### 1. 文件操作类（共89次）

**共性问题**：
- 中文文件名在Windows路径中的编码问题出现了12次
- 大文件读取时内存溢出出现了3次

**最佳实践**：
- 读取中文路径文件时，始终先检测编码再读取
- 大于10MB的文件采用Stream流式读取，不一次性加载

**用户偏好**：
- 用户倾向于将输出文件保存在桌面而非工作目录
- 用户偏好用Markdown格式输出报告，而非纯文本

**高频错误与规避**：
- ❌ 直接用UTF-8打开GBK编码文件 → ✅ 先用StreamReader自动检测编码
- ❌ 覆盖同名文件不提示 → ✅ 始终先询问用户是否覆盖

### 2. 网页抓取类（共56次）
...

### 3. 代码生成类（共42次）
...

---

## 二、全局改进建议

1. **编码处理模块**需要升级，建议内置更完善的编码检测库
2. **文件输出路径**应增加"记住上次保存位置"功能
3. **网页抓取**的反爬处理成功率仅72%，需要增加代理IP轮换能力
4. **代码执行沙箱**的超时时间需从5分钟调至10分钟（复杂任务经常超时）

---

## 三、核心经验库更新

本次升级将以下经验写入核心经验库（永久生效）：

| 编号 | 经验规则 | 来源 | 置信度 |
|------|---------|------|--------|
| EXP-047 | Windows中文路径始终用Path.GetFullPath处理 | 12次成功验证 | 98% |
| EXP-048 | 用户偏好Markdown格式输出 | 用户连续8次修改意见 | 95% |
| EXP-049 | 网页抓取先检测robots.txt | 3次失败教训 | 90% |
| ... | ... | ... | ... |
```

### 5.3 核心经验库

升级学习后，高置信度的经验会被提取为"核心经验"，存入系统级知识库。这些核心经验在每次执行任务时都会被自动加载到System Prompt中，无需再次检索。

```sql
CREATE TABLE core_experience (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    exp_code        TEXT UNIQUE,             -- 经验编号 EXP-001
    rule            TEXT NOT NULL,           -- 经验规则（一句话）
    detail          TEXT,                    -- 详细说明
    source_count    INTEGER DEFAULT 1,       -- 来源任务数量
    confidence      REAL DEFAULT 0.5,        -- 置信度 0-1
    category        TEXT,                    -- 类别
    created_at      DATETIME,
    updated_at      DATETIME,
    active          BOOLEAN DEFAULT TRUE     -- 是否生效
);
```

---

## 六、R4：AI控制台

### 6.1 控制台界面布局

```
┌─────────────────────────────────────────────────────────────────┐
│  🤖 AI 控制台                                    ClawBY19 v2.0  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─ 当前模型 ──────────────────────────────────────────────┐    │
│  │  🟢 DeepSeek-V3  [Claw自选]    API状态: 正常  延迟:120ms │    │
│  │  Endpoint: api.deepseek.com      上下文窗口: 128K         │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌─ 本次任务消耗 ──────────┐  ┌─ 历史消耗（大模型使用费）──┐    │
│  │ Input Tokens:   1,247   │  │ 今日:    ¥ 2.35           │    │
│  │ Output Tokens:    856   │  │ 本周:    ¥ 12.80          │    │
│  │ 总计 Tokens:    2,103   │  │ 本月:    ¥ 47.60          │    │
│  │ 本次费用:    ¥ 0.0042   │  │ 累计:    ¥ 156.20         │    │
│  └──────────────────────────┘  └────────────────────────────┘    │
│                                                                 │
│  ┌─ 使用统计 ───────────────────────────────────────────────┐    │
│  │ 请求次数     │ 今日: 23    本周: 145    本月: 580        │    │
│  │ 成功次数     │ 今日: 22    本周: 140    本月: 563        │    │
│  │ 资源消耗     │ CPU: 12%   内存: 340MB  磁盘: 128MB      │    │
│  │ 统计额度     │ 日限额: ¥50  月限额: ¥500  剩余: ¥452    │    │
│  │ 统计Tokens   │ 今日: 45.2K  本月: 1.8M  累计: 12.6M    │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌─ 性能指标 ──────────────────────────────────────────────┐    │
│  │ 平均 RPM (请求/分钟):   3.2                             │    │
│  │ 平均 TPM (Token/分钟):  4,580                           │    │
│  │ 平均响应延迟:            1.2s                            │    │
│  │ 平均首Token延迟:         0.3s                            │    │
│  │ 成功率:                  97.2%                           │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌─ 模型费用分析 ──── [当天] [本周] [本月] [本年] ─────────┐    │
│  │（LiveCharts2 WPF柱状图，按模型堆叠，鼠标悬停显示明细）   │    │
│  │  ¥                                                      │    │
│  │ 0.8│          ██                                        │    │
│  │ 0.6│       ██ ██ ██                                     │    │
│  │ 0.4│    ██ ██ ██ ██ ██                                  │    │
│  │ 0.2│ ██ ██ ██ ██ ██ ██ ██                    ██ ██      │    │
│  │ 0.0├──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──    │    │
│  │    0  1  2  3  4  5  6  7  8  9  10 ... 20 21 22 23     │    │
│  │                      时间（小时）                        │    │
│  │  图例: ██ DeepSeek-V3  ██ Qwen-Max  ██ 本地Ollama       │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 费用计算引擎

内置各主流模型的收费标准（可在线更新，与第二章模型清单联动）：

```toml
# config/model_pricing.toml — 模型收费标准配置

# ----------------------------------------------------------
# 中国开源模型（API调用计费）
# ----------------------------------------------------------
[deepseek-v3]
provider = "深度求索"
input_price_per_million = 1.0
output_price_per_million = 2.0
cache_hit_price_per_million = 0.1
currency = "CNY"
last_updated = "2026-04-01"

[deepseek-r1]
provider = "深度求索"
input_price_per_million = 4.0
output_price_per_million = 16.0
currency = "CNY"
last_updated = "2026-04-01"

[qwen3-235b]
provider = "阿里云百炼"
input_price_per_million = 5.0
output_price_per_million = 15.0
currency = "CNY"
last_updated = "2026-04-01"

[qwen2.5-72b]
provider = "阿里云百炼"
input_price_per_million = 4.0
output_price_per_million = 12.0
currency = "CNY"
last_updated = "2026-04-01"

[qwq-32b]
provider = "阿里云百炼"
input_price_per_million = 2.0
output_price_per_million = 8.0
currency = "CNY"
last_updated = "2026-04-01"

[qwen2.5-coder-32b]
provider = "阿里云百炼"
input_price_per_million = 3.5
output_price_per_million = 7.0
currency = "CNY"
last_updated = "2026-04-01"

# ----------------------------------------------------------
# 中国商业API模型
# ----------------------------------------------------------
[qwen-max]
provider = "阿里云百炼"
input_price_per_million = 20.0
output_price_per_million = 60.0
currency = "CNY"
last_updated = "2026-04-01"

[qwen-plus]
provider = "阿里云百炼"
input_price_per_million = 4.0
output_price_per_million = 12.0
currency = "CNY"
last_updated = "2026-04-01"

[qwen-turbo]
provider = "阿里云百炼"
input_price_per_million = 0.3
output_price_per_million = 0.6
currency = "CNY"
last_updated = "2026-04-01"

[glm-4-plus]
provider = "智谱AI"
input_price_per_million = 20.0
output_price_per_million = 20.0
currency = "CNY"
last_updated = "2026-04-01"

[glm-z1-flash]
provider = "智谱AI"
input_price_per_million = 10.0
output_price_per_million = 10.0
currency = "CNY"
last_updated = "2026-04-01"

[moonshot-v1-128k]
provider = "月之暗面"
input_price_per_million = 12.0
output_price_per_million = 12.0
currency = "CNY"
last_updated = "2026-04-01"

[minimax-text-01]
provider = "MiniMax"
input_price_per_million = 1.0
output_price_per_million = 8.0
currency = "CNY"
last_updated = "2026-04-01"

[spark4.0-ultra]
provider = "科大讯飞"
input_price_per_million = 30.0
output_price_per_million = 30.0
currency = "CNY"
last_updated = "2026-04-01"

[ernie-4.5]
provider = "百度文心"
input_price_per_million = 4.0
output_price_per_million = 8.0
currency = "CNY"
last_updated = "2026-04-01"

[doubao-pro-32k]
provider = "字节豆包"
input_price_per_million = 0.8
output_price_per_million = 2.0
currency = "CNY"
last_updated = "2026-04-01"

[hunyuan-turbo]
provider = "腾讯混元"
input_price_per_million = 14.9
output_price_per_million = 49.0
currency = "CNY"
last_updated = "2026-04-01"

[baichuan4-turbo]
provider = "百川智能"
input_price_per_million = 15.0
output_price_per_million = 15.0
currency = "CNY"
last_updated = "2026-04-01"

# ----------------------------------------------------------
# 美国主流模型
# ----------------------------------------------------------
[claude-sonnet-4-6]
provider = "Anthropic"
input_price_per_million = 21.0       # $3/M x 7.0汇率
output_price_per_million = 105.0     # $15/M x 7.0汇率
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[claude-opus-4-6]
provider = "Anthropic"
input_price_per_million = 105.0      # $15/M
output_price_per_million = 525.0     # $75/M
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[claude-haiku-4-5]
provider = "Anthropic"
input_price_per_million = 2.1        # $0.3/M
output_price_per_million = 10.5      # $1.5/M
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[gpt-4o]
provider = "OpenAI"
input_price_per_million = 17.5       # $2.5/M
output_price_per_million = 52.5      # $7.5/M
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[o3]
provider = "OpenAI"
input_price_per_million = 140.0      # $20/M
output_price_per_million = 560.0     # $80/M
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[o4-mini]
provider = "OpenAI"
input_price_per_million = 7.7        # $1.1/M
output_price_per_million = 31.0      # $4.4/M
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[gemini-2.5-pro]
provider = "Google"
input_price_per_million = 25.0
output_price_per_million = 105.0
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[gemini-2.0-flash]
provider = "Google"
input_price_per_million = 1.4
output_price_per_million = 4.2
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[grok-3]
provider = "xAI"
input_price_per_million = 35.0
output_price_per_million = 105.0
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

[mistral-large-3]
provider = "Mistral AI"
input_price_per_million = 14.0
output_price_per_million = 42.0
currency = "CNY"
exchange_rate = 7.0
last_updated = "2026-04-01"

# ----------------------------------------------------------
# 本地Ollama模型（零费用）
# ----------------------------------------------------------
[ollama/deepseek-r1:8b]
provider = "本地Ollama"
input_price_per_million = 0
output_price_per_million = 0
currency = "CNY"
note = "本地GPU/CPU运行，电费忽略不计"

[ollama/qwen2.5:7b]
provider = "本地Ollama"
input_price_per_million = 0
output_price_per_million = 0
currency = "CNY"
note = "本地GPU/CPU运行，电费忽略不计"

# 其余Ollama模型同上，价格均为0
```

### 6.3 费用计算公式

```
单次费用 = (input_tokens / 1,000,000) × input_price 
         + (output_tokens / 1,000,000) × output_price
```

### 6.4 统计数据表

```sql
-- 每次API调用的详细记录
CREATE TABLE api_usage_log (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp       DATETIME DEFAULT (datetime('now','localtime')),
    task_id         TEXT,                   -- 关联的任务ID
    model           TEXT NOT NULL,          -- 使用的模型ID（对应model_pricing.toml）
    provider        TEXT,                   -- 提供商
    selection_mode  TEXT,                   -- user_specified 或 claw_auto
    tokens_input    INTEGER DEFAULT 0,
    tokens_output   INTEGER DEFAULT 0,
    tokens_total    INTEGER DEFAULT 0,
    cost_cny        REAL DEFAULT 0,         -- 费用（元）
    latency_ms      INTEGER,                -- 响应延迟（毫秒）
    ttft_ms         INTEGER,                -- 首Token延迟
    status          TEXT DEFAULT 'success', -- success/error/timeout
    error_message   TEXT                    -- 错误信息（如有）
);

-- 按小时聚合的统计视图（用于LiveCharts2柱状图）
CREATE VIEW hourly_cost AS
SELECT 
    date(timestamp) as date,
    cast(strftime('%H', timestamp) as INTEGER) as hour,
    model,
    COUNT(*) as request_count,
    SUM(tokens_total) as total_tokens,
    SUM(cost_cny) as total_cost
FROM api_usage_log
GROUP BY date, hour, model;
```

### 6.5 柱状图时间轴切换

| 时间选项 | X轴 | 聚合粒度 | 数据范围 |
|---------|-----|---------|---------|
| 当天 | 0-23时 | 每小时 | 今日0:00至当前 |
| 本周 | 周一~周日 | 每天 | 本周一至当前 |
| 本月 | 1~31日 | 每天 | 本月1日至当前 |
| 本年 | 1~12月 | 每月 | 今年1月至当前 |

柱状图使用**LiveCharts2（WPF版）**渲染，支持：
- 鼠标悬停显示具体费用数值
- 按模型分颜色堆叠显示
- 点击某根柱子可下钻查看该时段的详细调用记录

---

## 七、R5：多级费用预警 · 闪灯机制

### 7.1 预警配置

```toml
# config/alert_rules.toml

[daily]
enabled = true
threshold_cny = 20.0           # 每日消耗预警：20元
warning_at_percent = 80        # 达到80%时黄色预警
critical_at_percent = 100      # 达到100%时红色预警并暂停

[weekly]
enabled = true
threshold_cny = 100.0          # 每周消耗预警：100元
warning_at_percent = 80
critical_at_percent = 100

[monthly]
enabled = true
threshold_cny = 500.0          # 每月消耗预警：500元
warning_at_percent = 80
critical_at_percent = 100

[cumulative]
enabled = true
threshold_cny = 5000.0         # 累计消耗预警：5000元
warning_at_percent = 90
critical_at_percent = 100

[action]
on_warning = "flash_yellow"              # 黄色预警动作
on_critical = "flash_red_and_pause"      # 红色预警：闪灯+暂停执行
auto_pause_on_critical = true            # 达到红色预警时是否自动暂停
```

### 7.2 闪灯机制实现

预警触发后，主界面顶部出现闪烁提示条：

```
┌─────────────────────────────────────────────────────────────┐
│ ⚠️ 🔴 费用预警：今日已消耗 ¥18.50 / ¥20.00 (92.5%)          │
│    本月已消耗 ¥425.00 / ¥500.00 (85.0%)   [查看详情] [关闭] │
├─────────────────────────────────────────────────────────────┤
│                        正常主界面                             │
│                          ...                                 │
└─────────────────────────────────────────────────────────────┘
```

**闪灯效果**：
- **黄色预警（80%阈值）**：顶部黄色提示条，慢速闪烁（1秒间隔），Windows任务栏图标变黄
- **红色预警（100%阈值）**：顶部红色提示条，快速闪烁（0.3秒间隔），Windows任务栏图标变红闪烁，系统托盘弹出通知
- **红色预警可选暂停**：达到红色阈值时，可配置自动暂停所有AI调用（需用户手动解除或调高阈值）

### 7.3 预警检查时机

- 每次API调用完成后立即检查
- 每分钟后台定时检查（C# PeriodicTimer，防止多任务并行时遗漏）
- 每日0:00自动重置日度计数器
- 每周一0:00自动重置周度计数器
- 每月1日0:00自动重置月度计数器

---

## 八、OpenClaw功能完整覆盖清单

| OpenClaw功能 | ClawBY19覆盖 | 实现方式 |
|-------------|-------------|---------|
| 文件系统读写 | ✅ | .NET System.IO 原生操作 |
| 代码执行（Python/JS/Shell） | ✅ | 嵌入式Python + Process.Start子进程 + PowerShell |
| 浏览器自动化 | ✅ | Microsoft.Playwright（按需加载） |
| 网页抓取与解析 | ✅ | HttpClient + HtmlAgilityPack清洗 |
| 搜索工具 | ✅ | Tavily/Brave多源搜索 |
| MCP协议支持 | ✅ | C# MCP客户端（WebSocket/HTTP） |
| Skills安装和管理 | ✅ | 自然语言安装 + 动态程序集加载 |
| 多渠道消息集成 | ✅ | 飞书/企微/钉钉 + Telegram/Discord |
| Agent Loop（ReAct） | ✅ | C# async/await + Channel<T>任务编排引擎 |
| 权限管控 | ✅ | 三级权限 + 操作确认对话框 |
| 会话管理 | ✅ | 多会话并行 + SQLite历史回溯 |
| 心跳轮询（后台任务） | ✅ | C# PeriodicTimer 异步定时器 |
| 项目记忆 | ✅ | CLAWBY19.md 项目配置文件 |
| **以下为ClawBY19独有** | | |
| RAG学习笔记库 | ✅ | SQLite + sqlite-vec向量索引 |
| 人机交互反馈记录 | ✅ | 每次修改意见入库（EF Core） |
| 成功执行四维总结 | ✅ | 自动生成问题/方案/步骤/避错 |
| 自我学习升级 | ✅ | 遍历RAG库生成升级报告 |
| AI控制台（Token/费用/图表） | ✅ | 实时统计 + LiveCharts2柱状图 |
| 多级费用预警闪灯 | ✅ | 日/周/月/累计四级阈值 |
| **大模型支持矩阵（V2.0新增）** | | |
| 中国开源模型优先选项（A类） | ✅ | 11款开源模型内置支持 |
| 中国商业API模型（B类） | ✅ | 12款商业模型内置支持 |
| 美国主流模型（C类） | ✅ | 10款模型内置支持 |
| 本地Ollama模型（D类） | ✅ | 9款本地模型预配置 |
| **Claw模型选择模式（V2.0新增）** | | |
| {指定模型}模式 | ✅ | models.toml后台配置，指定任意模型ID |
| {Claw自选}模式 | ✅ | 任务路由+预算感��+降级链自动选型 |

---

## 九、开发计划

### 9.1 分期交付

| 阶段 | 周期 | 交付内容 |
|------|------|---------|
| **一期：C#核心引擎** | 3周 | Agent Loop + 文件操作 + 代码沙箱 + CLI交互 + SQLite/EF Core数据层 |
| **二期：WPF GUI** | 3周 | 主界面 + 聊天窗口 + 实时流式输出（Channel<T>）+ 系统托盘 |
| **三期：模型矩阵+选择模式** | 2周 | 全模型清单接入 + {指定模型}/{Claw自选}两种模式 + 路由规则引擎 |
| **四期：RAG学习系统** | 4周 | 学习笔记库 + 人机交互反馈 + 成功总结生成 + sqlite-vec向量检索 + 升级学习 |
| **五期：AI控制台** | 3周 | 费用统计引擎 + WPF控制台UI + LiveCharts2柱状图 + 时间轴切换 + 收费配置 |
| **六期：费用预警** | 1周 | 四级预警阈值 + WPF闪灯机制 + 自动暂停 |
| **七期：浏览器+MCP+Skills** | 4周 | Playwright集成 + MCP连接器 + Skills动态加载 + OpenClaw兼容 |
| **八期：消息渠道+打包** | 2周 | 飞书/企微接入 + Inno Setup打包 + 安装程序 + 文档 |
| **合计** | **22周（约5.5个月）** | |

### 9.2 团队配置

| 角色 | 人数 | 技术栈 |
|------|------|--------|
| C#全栈工程师 | 2 | C# .NET 9 + WPF/MVVM + SQLite/EF Core + async/await + Playwright |
| UI工程师 | 1 | WPF XAML + LiveCharts2 + 控件样式 + 动画 |
| AI工程师 | 1 | Prompt工程 + RAG + sqlite-vec + 模型接入 + 路由规则设计 |
| **合计** | **4人** | |

---

## 十、运行环境要求

| 项目 | 最低配置 | 推荐配置 |
|------|---------|---------|
| 操作系统 | Windows 10 1903+ | Windows 11 |
| .NET运行时 | 随ClawBY19.exe自包含，无需预装 | .NET 9 自包含单文件，开箱即用 |
| CPU | 双核 2GHz | 四核 3GHz+ |
| 内存 | 4GB | 8GB+（本地Ollama模型需8GB+） |
| 磁盘空间 | 200MB（安装后） | 1GB+（含学习数据+Ollama模型） |
| 网络 | 需要（调用云端模型API） | 宽带或4G/5G（本地模式可离线） |
| GPU | 不需要（云端模型） | 有GPU可加速本地Ollama模型（CUDA/ROCm） |
| 运行时依赖 | WebView2 Runtime（Win10/11已预装，Playwright使用） | 无额外依赖 |

---

## 十一、与竞品的核心差异

```
            体积小         学习能力        费用管控        模型丰富度
              │               │               │               │
  OpenClaw    ■■■■■■■■■       ○               ○               ■■■■
  (500MB+)    (大)         (无)            (无)         (Claude主导)
              │               │               │               │
  ClawBY19    ■■■             ■■■■■■■■■       ■■■■■■■■■       ■■■■■■■■■
  (<100MB)    (小)        (RAG自学习)    (控制台+预警)  (中美42款+本地)
              │               │               │               │
              ▼               ▼               ▼               ▼
         C# .NET 9       每次执行都学习   四级阈值闪灯预警  中国开源优先
         WPF原生GUI      不重复犯错       按小时费用图表    Claw智能选型
         SQLite嵌入式    升级学习功能     自动暂停保护      本地Ollama零成本
```

---

> **附注1**：本方案中的模型收费标准基于2026年4月公开信息，实际价格可能随服务商调整而变化。`model_pricing.toml`支持在线自动更新或手动修改，确保费用计算始终准确。  
> **附注2**：开发语言由Rust调整为C#（.NET 9），数据库选型为SQLite（Microsoft.Data.Sqlite + EF Core + sqlite-vec），两者均为V2.0更新内容。  
> **附注3**：大模型支持清单和Claw模型选择模式为V2.0新增核心功能，所有配置均可通过`config/models.toml`后台文件热更新，无需重启程序。

---

## 十二、界面布局与交互设计（V2.1 实现规范）

> **版本**：V2.1  
> **日期**：2026年4月  
> **状态**：已实现（代码已编译发布）

### 12.1 主窗口布局

主窗口采用**左右分栏**设计，左侧为导航菜单，右侧为内容区，中间可拖动调整宽度。

```
┌─────────────────────────────────────────────────────────┐
│ [费用预警条 - 条件显示]                                    │
├──────────────┬──┬──────────────────────────────────────┤
│              │  │                                        │
│  🦞 ClawBY19 │  │                                        │
│  v2.0        │  │       右侧内容区（随菜单切换）            │
│              │  │                                        │
│  💬 聊天      │  │                                        │
│  📊 AI控制台  │G │                                        │
│  📋 任务      │r │                                        │
│  ⚙️ 设置      │i │                                        │
│              │d │                                        │
│              │S │                                        │
│  ●当前模型    │p │                                        │
│  Ocean       │  │                                        │
└──────────────┴──┴──────────────────────────────────────┘
```

**布局规格：**

| 区域 | 宽度 | 说明 |
|------|------|------|
| 左侧菜单 | 默认 180px，可拖动至 120~300px | `Grid.ColumnDefinitions` 含 `MinWidth/MaxWidth` |
| GridSplitter | 4px | 鼠标悬停变 SizeWE 光标 |
| 右侧内容 | `*`（剩余全部） | 随左侧拖动自适应 |

**左侧菜单底部固定显示：**
- 绿色圆点 + 当前选用模型名称（截断显示）
- 当前界面主题名称（如 `Ocean`），字号 10，半透明

**菜单激活状态：** 当前激活菜单项背景变为 `BgCardBrush`，文字变为 `AccentBlueBrush`。

---

### 12.2 聊天界面

#### 12.2.1 顶部模型切换栏

聊天页顶部固定一条模型状态栏：

```
┌─────────────────────────────────────────────────────┐
│ 🤖 DeepSeek-V3 ▾  [Claw自选]           ➕ 新会话    │
└─────────────────────────────────────────────────────┘
```

- 点击模型名/图标区域，弹出 **Popup 模型选择器**
- Popup 显示全部 42 款模型，按 A/B/C/D 分类标签显示
- 已配置 API Key 的模型正常可选；**未配置 API Key 的模型灰色不可选**
- 选中后立即切换，Popup 自动关闭

#### 12.2.2 昵称功能

- **首次进入**聊天界面：自动弹出昵称输入对话框（带半透明遮罩，居中显示）
- 用户输入昵称后点"确认"或按 Enter，昵称持久化写入 `config/appsettings.json`
- 点"跳过"则使用默认称谓
- **每次进入聊天 / 新会话**，ClawBY19 以昵称打招呼并告知当前使用的大模型：

  > 你好，**{昵称}**！我是 ClawBY19 🦞  
  > 当前使用 **{模型全名}** 协助你完成工作。

#### 12.2.3 消息气泡

| 角色 | 对齐 | 气泡颜色 | 圆角 |
|------|------|---------|------|
| 用户 | 右对齐 | `UserBubbleBgBrush` | `12,2,12,12` |
| AI | 左对齐 | `BgCardBrush` | `2,12,12,12` |

AI 气泡下方显示：时间戳 + 本次费用（¥0.0042 格式）。

---

### 12.3 AI控制台界面

保持现有设计（V2.0），包含：
- 今日消耗大字卡片 + 历史费用（本周/本月/累计）
- 模型费用柱状图（当天/本周/本月时间轴切换）
- 刷新按钮

---

### 12.4 任务界面

#### 12.4.1 总体布局

```
┌────────────────────────────────────────────┐
│ 📋 任务                           🔄 刷新   │
├────────────────────────────────────────────┤
│ [⚡ 正在执行] [📜 执行日志]                  │
├────────────────────────────────────────────┤
│                                            │
│         DataGrid 表格内容区                 │
│                                            │
└────────────────────────────────────────────┘
```

#### 12.4.2 正在执行任务（Tab 1）

数据来源：`LearningNotes` 表中 `Status = "running"` 的记录。

| 列名 | 绑定字段 | 宽度 | 说明 |
|------|---------|------|------|
| 任务内容 | `UserPrompt` | `*`（自适应） | 超长截断，悬停 Tooltip 显示全文 |
| 执行规则 | `TaskType` | 160px | 如"代码生成"、"文件操作"等 |
| 已执行次数 | `RevisionCount + 1` | 100px | 居中，绿色字体 |

列宽均支持鼠标拖动调整（`DataGrid` 默认行为）。

#### 12.4.3 任务执行日志（Tab 2）

数据来源：`LearningNotes` 表中 `Status = "success" | "failed"` 的记录，最新 200 条，按创建时间降序。

| 列名 | 绑定字段 | 宽度 | 说明 |
|------|---------|------|------|
| 任务内容 | `UserPrompt` | `2*` | 超长截断，悬停 Tooltip |
| 执行规则 | `TaskType` | 120px | — |
| 执行时间 | `CreatedAt` | 160px | 格式：`yyyy-MM-dd HH:mm:ss`，灰色 |
| 执行结果 | 成功/失败内容 | `3*` | 成功绿色：显示 `SummarySolution` 或 `FinalResult`；失败红色：显示 `ExecutionLog` |

---

### 12.5 设置界面

#### 12.5.1 总体布局

设置页采用**左右分栏**：左侧为模型 ListBox，右侧为参数配置区。

```
┌─────────────────────────────────────────────────────────────┐
│ ⚙️ 设置                [当前大模型选用 XXX ✓]  [💾 保存设置] │
├──────────────────┬──┬──────────────────────────────────────┤
│ 选择模型          │  │  🤖 DeepSeek-V3                       │
│                  │  │  深度求索                               │
│ DeepSeek-V3  ●  │  │  API Base URL: https://api.deepseek.com│
│ DeepSeek-R1  ●  │G │  API Key:  [___________________]        │
│ Qwen3-235B   ●  │r │                                        │
│ ...           ●  │i │  综合能力强，极高性价比...               │
│ GPT-4o       ●  │d │                                        │
│ Claude       ●  │S │  [🔌 连接测试]  ✅ 当前模型可用          │
│ Ollama       ●  │p │                                        │
│              ○  │  ├────────────────────────────────────────┤
│              ○  │  │ 模型选择模式 / 界面主题 / 费用预警 / 升级学习 │
└──────────────────┴──┴──────────────────────────────────────┘
```

#### 12.5.2 左侧模型 ListBox

- 显示全部 42 款模型，每项两行：模型全名（上）+ 分类标签（下，如"🇨🇳 A类：中国开源"）
- 右侧小圆点状态：**绿色** = 已配置 API Key 可用；**灰色** = 未配置不可用
- 点击某模型 → 右侧面板立即刷新为该模型的参数

#### 12.5.3 右侧模型参数面板

点击 ListBox 中任意模型后，右侧面板动态显示以下内容：

| 参数项 | 显示条件 | 说明 |
|--------|---------|------|
| 模型全名 + Provider | 始终 | 大号标题 |
| API Base URL | 始终 | 只读展示 |
| API Key | 非 Ollama 本地模型 | 明文 TextBox，可直接编辑 |
| Endpoint ID | 仅豆包（Doubao） | ep-XXXXXX 格式 |
| Ollama 提示 | 仅本地模型 | 提示确保 localhost:11434 运行 |
| 模型描述 | 始终 | 灰色小字 |
| 连接测试按钮 | 始终 | 见下方说明 |

**连接测试逻辑：**

- **云端模型**：向对应 API Base URL 发送一条极短请求（`max_tokens=1`），超时 10 秒
  - 成功：显示 `✅ {模型名} 连接正常，当前模型可用`（绿色）
  - 失败：显示 `❌ 连接失败（状态码）：{错误信息}`（红色）
  - 超时：显示 `❌ 连接超时（>10s），请检查网络或 API 地址`
- **本地 Ollama 模型**：GET `http://localhost:11434/api/tags`
  - 成功：显示 `✅ 本地 Ollama 服务运行正常，模型可用`
  - 失败：显示 `❌ Ollama 服务未响应：{状态码}`

**保存设置：** 点击"💾 保存设置"后：
1. 所有参数写入 `config/appsettings.json`
2. 模型选择模式写入 `config/models.toml`
3. 预警阈值写入 `config/alert_rules.toml`
4. 顶部状态显示：`设置已保存 ✓  当前大模型选用：{modelId}`

#### 12.5.4 模型选择模式

| 模式 | 行为 |
|------|------|
| `{Claw自选}` | 根据任务类型/预算/可用性自动路由（见第二章） |
| `{指定模型}` | 全局固定使用 ListBox 当前选中的模型 |

#### 12.5.5 界面主题

4 个主题按钮横排，点选后立即全局生效（`DynamicResource` 热更新），无需重启：

| 主题 | 特征 |
|------|------|
| ⬛ Dark（默认） | 深黑蓝紫 |
| 🌊 Ocean | 深海军蓝 + 青色 |
| 💚 Matrix | 纯黑 + 矩阵绿（终端风） |
| ☀️ Light | 浅灰白 + 蓝色（日间模式） |

当前选中主题同步显示在**主窗口左下角**（`MainViewModel.CurrentThemeName`，由 `ThemeService.ThemeChanged` 事件驱动）。

---

### 12.6 关键实现说明

#### 12.6.1 涉及的核心文件

| 文件 | 说明 |
|------|------|
| `Views/MainWindow.xaml` | 左右分栏主布局，GridSplitter，底部主题名 |
| `ViewModels/MainViewModel.cs` | 新增 `Tasks` 枚举值、`ShowTasksCommand`、`CurrentThemeName` |
| `Views/TasksView.xaml` | 任务 Tab 视图，DataGrid 双表格 |
| `ViewModels/TasksViewModel.cs` | 任务数据从 `LearningNotes` 读取 |
| `Views/SettingsView.xaml` | 左右分栏设置页，ListBox + 参数面板 |
| `ViewModels/SettingsViewModel.cs` | `SelectModelItemCommand`、`TestConnectionCommand`、`ModelSettingItem` |
| `Views/ChatView.xaml` | 顶部模型切换栏，Popup 选择器，昵称对话框遮罩 |
| `ViewModels/ChatViewModel.cs` | `PickerModels`、`ToggleModelPickerCommand`、`ConfirmNickNameCommand` |
| `Services/ThemeService.cs` | 新增 `CurrentTheme` 属性和 `ThemeChanged` 事件 |
| `Config/AppSettings.cs` | 新增 `NickName` 字段 |
| `App.xaml` | 新增 `SideNavButton` 样式（全宽左对齐导航按钮） |

#### 12.6.2 新增 SideNavButton 样式

```xml
<!-- 左侧菜单导航按钮：全宽，左对齐，圆角8，hover时背景变BgCardBrush -->
<Style x:Key="SideNavButton" TargetType="Button">
    <Setter Property="HorizontalAlignment" Value="Stretch"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Padding" Value="12,10"/>
    <Setter Property="CornerRadius" Value="8"/>
    ...
</Style>
```

#### 12.6.3 昵称存储位置

```json
// config/appsettings.json
{
  "NickName": "小明",
  "Theme": "Dark",
  ...
}
```

#### 12.6.4 任务数据映射

```
LearningNote.Status = "running"  →  正在执行任务 Tab
LearningNote.Status = "success"  →  执行日志 Tab（绿色，显示SummarySolution）
LearningNote.Status = "failed"   →  执行日志 Tab（红色，显示ExecutionLog）
```

---

## 十三、V2.2 进化记录（2026-04-07）

### 13.1 聊天输入框高度扩大

- `ChatView.xaml` 输入框改为多行：`AcceptsReturn="True"`、`MaxLines="6"`、`MinHeight="72"`（原为单行，约24px，现为72px，3倍高度）
- `TextWrapping="Wrap"` + `VerticalScrollBarVisibility="Auto"` 支持长文本自动换行
- 发送逻辑不变：Enter 发送，**Shift+Enter 换行**

### 13.2 附件上传按钮重设计

- 附件按钮由 `📎` 改为 `+`（FontSize=20，FontWeight=Light）
- 点击 `+` 弹出 Popup 菜单，包含三个选项：
  - 📄 **上传文件**：txt/md/json/csv/xml/cs/py/js/ts/html/css 等文本文件
  - 🖼 **上传图片**：png/jpg/jpeg/webp/gif
  - 🎬 **上传视频**：mp4/mov/avi/mkv/webm
- Popup 使用 `PlacementTarget` 定位在按钮上方，`StaysOpen="False"` 点击外部自动关闭
- `ChatViewModel` 新增三个 RelayCommand：`AddFileCommand`、`AddImageCommand`、`AddVideoCommand`
- `ChatView.xaml.cs` 新增 `AttachBtn_Click`（打开Popup）和 `AttachMenuItem_Click`（关闭Popup）

### 13.3 Claw 主题字体柔和化

`ClawTheme.xaml` 字体颜色由高饱和橘红调整为柔和橙色：

| 资源键 | 旧值 | 新值 |
|--------|------|------|
| `TextPrimaryBrush` | `#FFFF6B35` | `#FFFFA07A`（浅鲑橙） |
| `TextSecondaryBrush` | `#FF8B3A1A` | `#FFCD8B6A`（暖棕橙） |
| `ChatUserBrush` | `#FFFF8C42` | `#FFFFB899`（淡桃橙） |
| `ChatAssistantBrush` | `#FFFF6B35` | `#FFFFA07A`（浅鲑橙） |
| `ChatSystemBrush` | `#FF8B3A1A` | `#FFCD8B6A`（暖棕橙） |

---

## 十四、openClaw 智能体规范（V2.3，2026-04-07）

### 14.1 角色定位

ClawBY19 的聊天模块升级为 **openClaw 智能体**，系统提示词从通用助手扩展为专业智能体角色。

**角色定义：**
> 你是 openClaw 智能体 🦞，由 ClawBY19 驱动，具备任务执行、长期记忆和自我进化能力。

**核心能力：**
- 代码管理与 GitHub 自动同步（git add / commit / push）
- 云端部署与发布（SSH、FTP、Docker、云平台）
- AI 新闻采集、HTML 汇总、定时邮件发送
- 系统与代码安全监测、漏洞扫描、告警

**行为规则：**
- 每个任务必须：制定计划 → 执行 → 验收 → 学习总结
- 成功总结经验、失败分析根因、同类问题不再犯错
- 所有进化写入日志，可回溯；敏感操作须用户确认后执行
- 安全第一：不越权、不破坏、不泄露用户数据

**输出格式（每次任务响应）：**
```
任务理解 → 计划 → 执行 → 结果 → 学习总结 → 建议
```

---

### 14.2 操作历史与智能确认机制

#### 14.2.1 数据模型

新增 `OperationHistory` 实体，存储 GitHub 同步和云端部署的历史参数：

| 字段 | 类型 | 说明 |
|------|------|------|
| `OperationType` | string | `GitHub` 或 `CloudDeploy` |
| `TargetName` | string | 目标名称（仓库名/平台名） |
| `TargetUrl` | string | 目标地址（仓库URL/服务器IP） |
| `Account` | string | 账号（明文，非敏感） |
| `PasswordEncrypted` | string | AES-256加密后的Base64密码 |
| `LastUsedAt` | DateTime | 最近使用时间 |
| `UseCount` | int | 使用次数 |

#### 14.2.2 凭证加密方案

`OperationHistoryService` 使用 **AES-256** 加密密码，密钥由机器名+用户名+种子字符串的 SHA-256 哈希派生：

```csharp
var seed = $"ClawBY19:{Environment.MachineName}:{Environment.UserName}:OpHistKey";
byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
// 加密：AES.IV（16字节） + 密文 → Base64 存储
```

- 密码不进入 AI 上下文，仅明文账号和目标地址注入给 AI
- 每台机器的密钥唯一，无法跨机解密

#### 14.2.3 关键词检测规则

| 操作类型 | 触发关键词（含）|
|----------|----------------|
| GitHub | github / git push / git commit / 代码同步 / 同步到github / 推送代码 / 代码推送 / push代码 / 上传代码到github |
| CloudDeploy | 发布到云端 / 云端部署 / 部署到云 / 上传到云 / 发布到服务器 / 部署到服务器 / 云发布 / 上传服务器 / deploy |

检测发生在 `SendMessageAsync` 调用 AI 之前，用户消息预处理阶段。

---

### 14.3 交互流程规范

#### 14.3.1 有历史记录时（首次以外）

```
用户："帮我把代码同步到 github"
         ↓
系统检测到 GitHub 关键词
         ↓
查询 OperationHistory 找到上次记录
         ↓
弹出 GitHub 确认对话框：
┌─────────────────────────────────────────────┐
│  🐙 GitHub 同步确认                           │
│  检测到代码同步操作，是否使用上次执行的仓库？     │
│  ┌──────────────────────────────────────┐   │
│  │ 仓库：https://github.com/user/repo   │   │
│  │ 账号：user                           │   │
│  └──────────────────────────────────────┘   │
│  [ ✕ 取消 ]  [ 换新仓库 ]  [ ✅ 是的，就用这个 ]│
└─────────────────────────────────────────────┘
         ↓ 用户点击"是的，就用这个"
系统注入操作上下文到 AI 消息（不显示给用户）
AI 以 openClaw 智能体格式执行任务
```

#### 14.3.2 否定回复 / 首次操作

```
用户点击"换新仓库" 或 首次触发 GitHub 关键词
         ↓
弹出凭证输入对话框：
┌─────────────────────────────────────────────┐
│  🔑 输入 GitHub 仓库参数                       │
│  请填写执行参数，密码将加密保存供下次使用。        │
│  目标名称：[___________]                      │
│  目标地址：[https://github.com/用户名/仓库名]  │
│  账号：   [___________]                      │
│  密码/Token：[●●●●●●●●] （加密存储，不发给AI）│
│             [ ✕ 取消 ]  [ 确认执行 → ]         │
└─────────────────────────────────────────────┘
         ↓ 用户填写并点击"确认执行"
密码 AES 加密存入 OperationHistory 表
账号/地址注入 AI 上下文（明文，供 AI 执行）
AI 以 openClaw 智能体格式执行任务
下次相同操作将自动展示此记录供快速确认
```

#### 14.3.3 取消操作

- 点击"✕ 取消"：关闭对话框，取消本次发送，输入框内容保留
- 凭证对话框点"✕ 取消"：关闭，取消本次发送

---

### 14.4 注入上下文格式

当用户确认操作后，系统在原始输入末尾追加上下文标记（对 AI 可见，对用户不可见）：

```
{原始用户指令}

[操作上下文]
目标名称：我的项目
目标地址：https://github.com/user/my-project
账号：user
```

AI 的系统提示词包含说明：`当消息中包含 [操作上下文] 标记时，直接使用其中的信息执行操作，无需再次询问目标地址。`

---

### 14.5 新增/修改的关键文件

| 文件 | 变更内容 |
|------|---------|
| `Data/Entities/OperationHistory.cs` | 新建：操作历史实体 |
| `Services/OpenClaw/OperationHistoryService.cs` | 新建：历史CRUD + AES凭证加密 |
| `Data/ClawDbContext.cs` | 新增 `DbSet<OperationHistory>` 和两个索引 |
| `Services/Chat/ChatService.cs` | 系统提示词更新为 openClaw 智能体角色 |
| `ViewModels/ChatViewModel.cs` | 新增 GitHub/云端/凭证三组对话框属性和命令，重写 `SendMessageAsync` 加入关键词检测 |
| `Views/ChatView.xaml` | 新增 GitHub确认/云端确认/凭证输入 三个覆盖层对话框 |
| `Views/ChatView.xaml.cs` | 新增 `CredSubmitBtn_Click`（从 PasswordBox 读取密码传入 ViewModel） |
| `App.xaml.cs` | 注册 `OperationHistoryService` 为 Singleton |
