# 剧情播放系统 - 开发与使用文档

## 目录

1. [系统概述](#1-系统概述)
2. [架构设计](#2-架构设计)
3. [文件清单](#3-文件清单)
4. [Unity编辑器操作指南](#4-unity编辑器操作指南)
5. [JSON数据配置说明](#5-json数据配置说明)
6. [模板系统详解](#6-模板系统详解)
7. [触发器配置详解](#7-触发器配置详解)
8. [结果处理系统](#8-结果处理系统)
9. [集成到现有游戏流程](#9-集成到现有游戏流程)
10. [扩展指南](#10-扩展指南)
11. [常见问题](#11-常见问题)

---

## 1. 系统概述

剧情播放系统是一个可配置的过场对话系统，支持：

- **暂停游戏 → 播放文字 → 处理结果 → 恢复游戏** 的完整流程
- 通过 JSON 文件配置对话内容，方便策划填写
- 打字机效果逐字显示，支持玩家点击跳过/继续
- 多种触发方式：位置触发、关卡开始、波次完成、Boss战前后、标志位触发
- 头像（左/中/右）、图片展示区、多套文字样式
- 入场动画：角色从两侧旋转进入、对话框从pivot侧旋转进入
- 换边特效：左右说话者切换时播放百叶窗动画 + 颜色过渡
- 播放结束后可执行：生成敌人、改变状态、通关、游戏结束等

### 效果布局

```
┌──────────────────────────────────────────────┐
│                  游戏画面                      │
│         ┌──────────────────┐                  │
│         │   图片展示区域     │                  │
│         └──────────────────┘                  │
│                                               │
│ ┌──────┐ ┌────────────────────┐ ┌──────┐     │
│ │ 左侧 │ │                    │ │ 右侧 │     │
│ │ 头像 │ │   对话文字区域      │ │ 头像 │     │
│ │      │ │   [点击继续▼]      │ │      │     │
│ └──────┘ └────────────────────┘ └──────┘     │
└──────────────────────────────────────────────┘
```

---

## 2. 架构设计

### 核心组件关系

```
StoryManager (单例，控制播放流程)
    ├── StoryUI (UI状态管理 + 动画编排)
    │     └── StoryAnimator (动画执行器：旋转入场/百叶窗)
    ├── StoryTemplateLibrary (模板库)
    │     ├── StoryStyleTemplate[] (文字样式)
    │     ├── StoryAvatarTemplate[] (头像)
    │     └── StoryImageTemplate[] (图片)
    └── StoryDataCollection (JSON数据)
          └── StorySequence[] (剧情段落)
                └── StoryDialogue[] (单条对话)

StoryTrigger (场景中的触发器，多个)
    ├── 监听 LevelProgressManager 事件
    ├── 监听 BattleWaveManager 事件
    └── 调用 StoryManager.PlayStory()
```

### 播放流程

```
触发条件达成
    → StoryTrigger.TriggerStory()
        → OnBeforeStory 事件
        → StoryManager.PlayStory(storyId)
            → Time.timeScale = 0 (暂停游戏)
            → 显示 StoryUI
            → 入场动画 (如果配置了 StoryAnimator)
                → 扫描剧情找出左右头像和首个说话方向
                → 两侧角色 Y轴旋转进入
                → 对话框从首个说话者方向旋转进入
            → 逐条播放对话 (协程)
                → 检测说话者是否换边
                    → 是：播放百叶窗特效 + 颜色过渡
                    → 否：直接设置内容
                → 高亮说话者头像，暗化另一侧
                → 打字机效果显示文字
                → 等待玩家点击跳过或完成
                → 等待玩家点击继续
            → Time.timeScale = 1 (恢复游戏)
            → 隐藏 StoryUI
        → StoryTrigger.OnStoryComplete()
            → 执行 ResultActions (生成敌人/通关等)
            → OnAfterStory 事件
```

---

## 3. 文件清单

| 文件 | 路径 | 说明 |
|------|------|------|
| StoryData.cs | Scripts/StorySystem/ | 数据模型：对话、剧情序列、结果动作 |
| StoryTemplates.cs | Scripts/StorySystem/ | 模板系统：样式、头像、图片模板 + 模板库 |
| StoryManager.cs | Scripts/StorySystem/ | 核心管理器（单例）：播放控制、暂停恢复 |
| StoryUI.cs | Scripts/StorySystem/ | UI状态管理：对话框、头像、图片、打字机、动画编排 |
| StoryAnimator.cs | Scripts/StorySystem/ | 动画执行器：角色旋转入场、对话框旋转、百叶窗特效 |
| StoryTrigger.cs | Scripts/StorySystem/ | 触发器组件：多种触发方式 |
| StoryEditorWindow.cs | Editor/ | 编辑器工具窗口：JSON管理、模板创建 |
| story_chapter1.json | LevelData/StoryData/ | 示例JSON数据文件 |

---

## 4. Unity编辑器操作指南

### 4.1 初始搭建步骤

#### 第一步：创建模板库

1. 在 Project 窗口右键 → **Create → 剧情系统 → 模板库**
2. 命名为 `StoryTemplateLibrary`
3. 保存到 `Assets/LevelData/StoryData/` 目录

#### 第二步：批量创建样式模板

1. 打开菜单 **工具 → 剧情系统编辑器**
2. 选择 **"样式模板"** 标签页
3. 将刚创建的模板库拖入"模板库"字段
4. 点击 **"批量创建默认样式（主人公/Boss/小怪）"**
5. 系统自动创建4套样式并添加到模板库

#### 第三步：创建头像模板

1. 在编辑器窗口选择 **"头像模板"** 标签页
2. 填入头像代号（如 `hero_normal`）、名称、拖入头像图片
3. 点击 **"创建头像模板"**
4. 为每个需要显示的角色头像重复此步骤

#### 第四步：创建图片模板

1. 在编辑器窗口选择 **"图片模板"** 标签页
2. 填入图片代号（如 `bg_sunset`）、名称、拖入图片
3. 点击 **"创建图片模板"**

#### 第五步：搭建剧情UI（需要手动操作）

在场景中创建以下UI结构（Canvas下）：

```
Canvas
└── StoryRoot (空GameObject, 添加 CanvasGroup 组件)
    ├── ImageDisplayRoot (图片展示区)
    │   └── DisplayImage (Image组件, 居中偏上)
    ├── DialogueBox (对话框背景 Image, 底部居中)
    │   ├── SpeakerNameText (TextMeshPro - 说话者名称)
    │   ├── ContentText (TextMeshPro - 正文内容)
    │   ├── ContinueIndicator (提示标记, 如小三角▼)
    │   └── BlindsContainer (百叶窗遮罩容器, 详见第八步)
    ├── AvatarLeft (Image, 左下角)
    ├── AvatarCenter (Image, 底部居中, 默认隐藏)
    └── AvatarRight (Image, 右下角)
```

**推荐的UI参数：**

| 元素 | Anchor | 建议尺寸 |
|------|--------|---------|
| StoryRoot | 全屏拉伸 | - |
| ImageDisplayRoot | 上方居中 | 400×300 |
| DialogueBox | 底部拉伸 | 高度200, 左右留边距 |
| AvatarLeft | 左下 | 150×200 |
| AvatarRight | 右下 | 150×200 |
| ContentText | DialogueBox内拉伸 | 留padding |

#### 第六步：创建StoryManager

1. 在场景中创建空 GameObject，命名为 `StoryManager`
2. 添加 `StoryManager` 组件
3. 拖入引用：
   - **Story UI**: 拖入带 StoryUI 组件的对象
   - **Template Library**: 拖入模板库资源
   - **Story Json File**: 拖入 `story_chapter1.json`

#### 第七步：创建StoryUI

1. 在 StoryRoot 对象上添加 `StoryUI` 组件
2. 将UI元素拖入对应字段：
   - Story Root → StoryRoot对象
   - Dialogue Box Image → 对话框背景Image
   - Content Text → 正文TextMeshPro
   - Speaker Name Text → 说话者名称TextMeshPro
   - Avatar Left/Center/Right → 对应头像Image
   - Image Display Root → 图片展示区根节点
   - Display Image → 展示图片Image
   - Continue Indicator → 继续提示标记

#### 第八步：配置 StoryAnimator（入场动画 + 百叶窗特效）

> 这一步为**可选**。如果不配置 StoryAnimator，系统会使用原来的淡入淡出行为，不会报错。

##### 8-A. 创建 BlindsContainer（百叶窗遮罩容器）

1. 在 Hierarchy 中右键 **DialogueBox** → **UI → Empty Object**，命名为 `BlindsContainer`
2. 选中 `BlindsContainer`，在 Inspector 中如下设置 RectTransform：
   - 点击 Anchor Presets 左上角的小方块，**按住 Alt 键**点击 **右下角的 "stretch-stretch"** 图标（四个箭头向外的那个）
   - 确认 Left / Top / Right / Bottom 全部为 **0**（让它完全覆盖对话框）
3. **关闭它的 Image 组件**（如果自动添加了的话）：右键 `BlindsContainer` 的 Image 组件 → Remove Component
   - BlindsContainer 只需要 RectTransform，叶片会在运行时自动生成
4. 取消勾选 BlindsContainer 的 **激活状态**（Inspector 顶部的对勾），让它初始隐藏
   - 运行时由 StoryAnimator 自动控制显示/隐藏

##### 8-B. 挂载 StoryAnimator 组件

1. 在 Hierarchy 中选中 **StoryRoot** 节点（或其子节点，只要是同一 Canvas 下即可）
2. 在 Inspector 中点击 **Add Component**，搜索 `StoryAnimator`，添加它
3. 配置 StoryAnimator 组件的字段：

| Inspector 字段 | 说明 | 建议值 |
|---|---|---|
| Character Entrance Duration | 角色入场动画时长（秒） | 0.5 |
| Dialogue Box Entrance Duration | 对话框入场时长（秒） | 0.4 |
| Entrance Rotate Angle | 入场旋转起始角度（Y轴） | 90 |
| Entrance Curve | 动画曲线 | 默认 EaseInOut 即可 |
| Blinds Count | 百叶窗叶片数量 | 8 |
| Blinds Duration | 百叶窗过渡总时长（秒） | 0.6 |
| **Blinds Container** | **拖入上一步创建的 BlindsContainer** | （必填） |
| Blinds Curve | 百叶窗动画曲线 | 默认 EaseInOut 即可 |

4. 将 Hierarchy 中的 **BlindsContainer** 拖拽到 StoryAnimator 的 **Blinds Container** 字段

##### 8-C. 将 StoryAnimator 绑定到 StoryUI

1. 选中挂载了 **StoryUI** 组件的节点
2. 在 Inspector 中找到 StoryUI 的 **“过渡动画”** 分组
3. 将挂载了 StoryAnimator 的 GameObject 拖到 **Animator** 字段
4. 设置 **Inactive Speaker Color**（可选）：
   - 这是非活跃说话者的暗化颜色，默认为半透明灰色 `(0.5, 0.5, 0.5, 0.8)`
   - 如果希望暗化效果更明显，可调为 `(0.3, 0.3, 0.3, 0.6)`

##### 8-D. 验证配置是否正确

配置完成后，Inspector 中应看到如下引用关系（所有字段非 None）：

```
StoryUI (Inspector)
  ├─ 根画布
  │   └─ Story Root        → StoryRoot
  ├─ 对话框
  │   ├─ Dialogue Box Image → DialogueBox
  │   ├─ Content Text       → ContentText
  │   └─ Speaker Name Text  → SpeakerNameText
  ├─ 头像
  │   ├─ Avatar Left        → AvatarLeft
  │   ├─ Avatar Center      → AvatarCenter
  │   └─ Avatar Right       → AvatarRight
  ├─ 过渡动画
  │   ├─ Animator           → StoryRoot (挂载了 StoryAnimator 的节点)
  │   └─ Inactive Speaker Color → (0.5, 0.5, 0.5, 0.8)
  └─ ...

StoryAnimator (Inspector)
  ├─ 入场动画设置
  │   ├─ Character Entrance Duration  → 0.5
  │   ├─ Dialogue Box Entrance Duration → 0.4
  │   ├─ Entrance Rotate Angle        → 90
  │   └─ Entrance Curve               → EaseInOut
  └─ 百叶窗特效设置
      ├─ Blinds Count      → 8
      ├─ Blinds Duration   → 0.6
      ├─ Blinds Container  → BlindsContainer (❖ 必须拖入)
      └─ Blinds Curve      → EaseInOut
```

如果 Blinds Container 显示为 **None**，百叶窗特效将不会生效（但不会报错，仅跳过动画）。

##### 动画效果说明

| 动画 | 触发时机 | 效果描述 |
|------|---------|----------|
| 角色旋转入场 | 剧情开始时 | 左侧角色从左边缘 Y轴旋转 90°→ 0°，右侧角色从右边缘 -90°→0°，同时播放 |
| 对话框旋转入场 | 角色入场后 | pivot 移至对应侧边缘，Y轴旋转“翻开”进入。第一句话是左边人说就从左边翻，右边人说就从右边翻 |
| 百叶窗换边 | 说话者左右切换时 | 水平叶片从 0 高度扩展到全高→中间点切换内容和颜色→叶片收缩回 0。颜色从旧对话框色渐变到新对话框色 |
| 头像高亮/暗化 | 每句对话 | 当前说话者头像显示为原色，另一侧头像添加暗化色调 |

> **注意**：JSON 数据中的 `avatarPosition` 字段决定了说话者方向。当连续两句对话的 `avatarPosition` 不同时（如从 `"left"` 变为 `"right"`），系统自动触发百叶窗特效。

### 4.2 设置触发器

#### 位置触发（玩家走到某处触发）

1. 在场景中创建空 GameObject
2. 添加 `StoryTrigger` 组件
3. 设置：
   - Trigger Type: **Position**
   - Story Id: `boss_encounter`（对应JSON中的storyId）
   - Trigger Position X: 设置触发位置
   - Trigger Once: ✓

#### 关卡开始触发

1. 创建空 GameObject + `StoryTrigger` 组件
2. 设置：
   - Trigger Type: **LevelStart**
   - Story Id: `level1_opening`

#### Boss战后触发

1. 创建空 GameObject + `StoryTrigger` 组件
2. 设置：
   - Trigger Type: **AllWavesComplete**
   - Story Id: `boss_defeated`
3. 在 Result Actions 中添加结果动作：
   - Action Type: **LevelComplete**

#### 标志位触发（游戏内动态设置）

1. 创建触发器，Trigger Type: **Flag**，Flag Name: `boss_phase2`
2. 在任何脚本中调用: `StoryManager.Instance.SetFlag("boss_phase2");`
3. StoryTrigger 会自动检测到标志位并触发剧情

---

## 5. JSON数据配置说明

### 5.1 完整JSON结构

```json
{
    "stories": [
        {
            "storyId": "剧情唯一ID",
            "chapterId": "章ID",
            "sectionId": "节ID",
            "dialogues": [
                {
                    "id": 1,
                    "chapterId": "chapter_1",
                    "sectionId": "section_1",
                    "content": "对话正文内容",
                    "styleId": "样式模板ID",
                    "playSpeed": 0.05,
                    "avatarPosition": "left",
                    "avatarId": "头像模板ID",
                    "showImage": false,
                    "imageId": "",
                    "speakerName": "说话者名称",
                    "extraJson": ""
                }
            ]
        }
    ]
}
```

### 5.2 字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| **StorySequence** | | | |
| storyId | string | ✓ | 剧情段落唯一ID，触发器通过此ID引用 |
| chapterId | string | ✓ | 所属章ID（用于组织管理） |
| sectionId | string | ✓ | 所属节ID |
| dialogues | array | ✓ | 对话列表 |
| **StoryDialogue** | | | |
| id | int | ✓ | 该条对话的ID |
| chapterId | string | | 所属章ID |
| sectionId | string | | 所属节ID |
| content | string | ✓ | 正文内容 |
| styleId | string | ✓ | 文字样式ID（对应样式模板） |
| playSpeed | float | | 播放速度，每字符间隔秒数（默认0.05） |
| avatarPosition | string | | 头像位置：`left` / `center` / `right` |
| avatarId | string | | 头像代号（对应头像模板） |
| showImage | bool | | 是否显示图片 |
| imageId | string | | 图片代号（对应图片模板） |
| speakerName | string | | 说话者名称（留空则取样式模板默认值） |
| extraJson | string | | 扩展数据（预留字段） |

### 5.3 策划填写指南

1. 打开 **工具 → 剧情系统编辑器 → JSON导入/导出**
2. 点击 **"生成空白JSON模板文件"** 获取模板
3. 用任意文本编辑器编辑JSON文件
4. 将编辑好的JSON文件放入 `Assets/LevelData/StoryData/` 目录
5. 在编辑器窗口的 **"预览"** 标签页检查数据
6. 将JSON文件拖入 StoryManager 的 `Story Json File` 字段

---

## 6. 模板系统详解

### 6.1 文字样式模板 (StoryStyleTemplate)

每套样式定义了一个角色类型的文字表现，在 Unity Inspector 中可配置：

| 属性 | 说明 |
|------|------|
| Style Id | 与JSON中styleId匹配的唯一标识 |
| Display Name | 编辑器中的显示名称 |
| Font Size | 字体大小 |
| Text Color | 文字颜色 |
| Font | TMP字体资源（可选） |
| Dialogue Box Color | 对话框背景色 |
| Dialogue Box Sprite | 对话框背景图（可选，用于9宫格图） |
| Default Speaker Name | 默认说话者名称 |
| Speaker Name Color | 名称颜色 |
| Speaker Name Font Size | 名称字体大小 |
| Enable Outline | 是否启用文字描边 |
| Outline Color | 描边颜色 |
| Outline Width | 描边宽度 |

**预设的默认样式：**

| 样式ID | 对话框色 | 说明 |
|--------|---------|------|
| protagonist | 青色 (0, 0.8, 1) | 主人公 |
| boss | 红色 (0.8, 0.2, 0.2) | Boss |
| minion | 灰色 (0.5, 0.5, 0.5) | 小怪 |
| narrator | 黑色 (0, 0, 0) | 旁白 |

### 6.2 头像模板 (StoryAvatarTemplate)

| 属性 | 说明 |
|------|------|
| Avatar Id | 唯一代号 |
| Display Name | 显示名称 |
| Avatar Sprite | 头像精灵图 |
| Scale | 缩放比例 |

创建方式：
- 右键 → **Create → 剧情系统 → 头像模板**
- 或通过编辑器窗口快速创建

### 6.3 图片模板 (StoryImageTemplate)

| 属性 | 说明 |
|------|------|
| Image Id | 唯一代号 |
| Display Name | 显示名称 |
| Image Sprite | 图片精灵 |

### 6.4 模板库 (StoryTemplateLibrary)

将所有模板集中管理，运行时自动建立 ID → 模板的缓存映射：

- 右键 → **Create → 剧情系统 → 模板库**
- 将所有样式、头像、图片模板拖入对应列表
- 拖入 StoryManager 的 Template Library 字段

---

## 7. 触发器配置详解

### StoryTrigger 组件属性

| 属性 | 说明 |
|------|------|
| **触发配置** | |
| Trigger Type | 触发类型（见下表） |
| Story Id | 要播放的剧情ID |
| Trigger Once | 是否只触发一次 |
| **位置触发设置** | |
| Trigger Position X | 触发位置X坐标 |
| Trigger From Left | 方向：从左向右越过时触发 |
| **标志位触发设置** | |
| Flag Name | 监听的标志位名称 |
| **波次触发设置** | |
| Wave Index | 指定波次索引（-1=任意波次） |
| **播放后处理** | |
| Result Actions | 结果动作列表 |
| **事件** | |
| On Before Story | 剧情播放前的自定义事件 |
| On After Story | 剧情播放后的自定义事件 |

### 触发类型一览

| 类型 | 说明 | 适用场景 |
|------|------|---------|
| Position | 玩家到达指定X坐标 | 路途中的NPC对话、区域进入提示 |
| Flag | 标志位被设置时 | 游戏内动态条件、多条件组合触发 |
| LevelStart | 关卡开始时 | 关卡开场剧情 |
| WaveComplete | 指定波次完成时 | 中场剧情、阶段性对话 |
| AllWavesComplete | 所有波次完成时 | Boss战后剧情、通关前叙事 |
| Manual | 手动调用 | 由其它脚本控制触发 |

### Scene视图辅助

Position类型的触发器会在Scene视图中显示：
- **青色竖线**：触发位置
- **小三角**：触发方向
- **文字标签**：剧情ID

---

## 8. 结果处理系统

每个 StoryTrigger 可以配置多个 `StoryResultAction`，在剧情播放完毕后按顺序执行：

| 动作类型 | 说明 | 参数用法 |
|---------|------|---------|
| None | 无操作 | - |
| SpawnEnemies | 生成敌人（触发波次） | intParameter = 波次索引 |
| RemoveEnemies | 移除敌人 | - |
| ChangeEnemyState | 改变敌人状态 | 自定义处理 |
| GameOver | 游戏结束 | - |
| LevelComplete | 通关 | - |
| LoadScene | 加载场景 | parameter = 场景名 |
| SetFlag | 设置标志位 | parameter = 标志位名 |
| Custom | 自定义 | 通过 OnAfterStory 事件处理 |

### 示例：Boss战前后配置

**Boss战前触发器：**
- Trigger Type: Position
- Story Id: `boss_encounter`
- Result Actions:
  - [0] SpawnEnemies, intParameter: 3 (触发Boss波次)

**Boss战后触发器：**
- Trigger Type: AllWavesComplete
- Story Id: `boss_defeated`
- Result Actions:
  - [0] LevelComplete

---

## 9. 集成到现有游戏流程

### 与 LevelProgressManager 集成

StoryTrigger 自动监听 `LevelProgressManager.OnLevelStart` 事件：

```csharp
// 无需额外代码，只需在场景中放置 LevelStart 类型的 StoryTrigger 即可
```

### 与 BattleWaveManager 集成

StoryTrigger 自动监听 `BattleWaveManager` 的波次事件：

```csharp
// 无需额外代码，配置 WaveComplete / AllWavesComplete 类型触发器即可
```

### 在代码中手动触发

```csharp
// 方式1：直接通过StoryManager播放
StoryManager.Instance.PlayStory("boss_encounter", () => {
    Debug.Log("剧情播放完毕，继续游戏逻辑");
});

// 方式2：设置标志位，由Flag类型触发器自动检测
StoryManager.Instance.SetFlag("show_tutorial");

// 方式3：获取Manual类型触发器并手动触发
storyTrigger.TriggerManually();
```

### 在 BattleWaveManager 中集成剧情触发的示例

如果需要在特定波次前插入剧情，可以利用标志位：

```csharp
// 在波次开始时检查是否需要播放剧情
void OnWaveStarted(int waveIndex)
{
    if (waveIndex == 2) // 第3波前
    {
        StoryManager.Instance.SetFlag("before_wave3");
    }
}
```

---

## 10. 扩展指南

### 10.1 添加新的对话属性

1. 在 `StoryData.cs` 的 `StoryDialogue` 类中添加新字段：

```csharp
[Tooltip("新属性说明")]
public string newProperty;
```

2. JSON文件中添加对应字段（缺失字段会使用默认值，向前兼容）

3. 在 `StoryManager.SetupDialogueDisplay()` 中处理新属性

### 10.2 添加新的样式参数

1. 在 `StoryTemplates.cs` 的 `StoryStyleTemplate` 类中添加字段
2. 在 `StoryUI.ApplyStyle()` 中应用新样式

### 10.3 添加新的触发类型

1. 在 `StoryTrigger.TriggerType` 枚举中添加新类型
2. 在 `Start()` 中订阅对应事件
3. 在 `Update()` 或事件回调中实现检测逻辑

### 10.4 添加新的结果动作

1. 在 `StoryData.cs` 的 `StoryResultAction.ActionType` 中添加类型
2. 在 `StoryManager.ProcessResultAction()` 中添加处理逻辑

### 10.5 支持多语言

可以利用 `extraJson` 字段或扩展 `StoryDialogue` 增加:

```csharp
public string content_en;  // 英文内容
public string content_ja;  // 日文内容
```

然后在 `StoryManager` 中根据语言设置选择对应字段。

---

## 11. 常见问题

### Q: 剧情播放时游戏角色还在动？
A: StoryManager 会设置 `Time.timeScale = 0` 来暂停游戏。确保游戏逻辑使用 `Time.deltaTime` 而不是 `Time.unscaledDeltaTime`。UI动画（如打字机效果）使用 `WaitForSecondsRealtime` 不受 timeScale 影响。

### Q: JSON格式写错了怎么排查？
A: 打开 **工具 → 剧情系统编辑器**，在 JSON导入/导出 标签页使用 **"验证JSON格式"** 功能。

### Q: 如何让同一段剧情可以重复播放？
A: 在 StoryTrigger 上取消勾选 **Trigger Once**。

### Q: 打字机速度怎么调？
A: 在JSON中设置 `playSpeed` 字段（单位：秒/字符，越小越快）。0.05 比较适中，0.02 很快，0.08 较慢。

### Q: 如何在不同场景间共享剧情数据？
A: JSON文件和模板库都是 Asset 资源，可以在任何场景的 StoryManager 中引用同一份。

### Q: 手柄如何操作？
A: 系统自动支持鼠标左键、键盘 Space/Enter、手柄南键（A/×）来跳过/继续。

---

## 附录：快速设置清单

- [ ] 创建 StoryTemplateLibrary 资源
- [ ] 通过编辑器窗口批量创建样式模板
- [ ] 创建所需的头像模板（准备好头像图片）
- [ ] 创建所需的图片模板（准备好展示图片）
- [ ] 搭建 StoryUI 层级结构（Canvas下）
- [ ] 在 StoryUI 组件中绑定所有UI引用
- [ ] 创建 StoryManager 对象并绑定引用
- [ ] 编写/导入 JSON 剧情数据文件
- [ ] 在场景中放置 StoryTrigger 触发器
- [ ] 配置触发器的 Result Actions
- [ ] （可选）创建 BlindsContainer（DialogueBox 子节点，Stretch 铺满）
- [ ] （可选）挂载 StoryAnimator 组件，拖入 BlindsContainer
- [ ] （可选）将 StoryAnimator 拖入 StoryUI 的 Animator 字段
- [ ] 运行测试
