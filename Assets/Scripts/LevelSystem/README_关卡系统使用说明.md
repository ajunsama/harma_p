# 关卡编辑系统使用说明

## 📁 新增文件列表

### 脚本文件 (Assets/Scripts/LevelSystem/)
- `LevelData.cs` - 关卡数据结构（ScriptableObject）
- `BattleWaveManager.cs` - 战斗波次管理器
- `InfiniteScrollBackground.cs` - 🆕 单张背景无限滚动（推荐）
- `ParallaxBackground.cs` - 多层视差滚动背景（可选）
- `LevelProgressManager.cs` - 关卡进度管理器（固定视角相机）
- `LevelUI.cs` - 关卡UI管理器
- `BattleAreaVisualizer.cs` - 战斗区域可视化

### 编辑器文件 (Assets/Editor/)
- `LevelEditorWindow.cs` - 关卡编辑器窗口

---

## 🎮 在Unity编辑器中的设置步骤

### 步骤1：创建关卡数据

1. 在Unity菜单栏点击 **Tools → 关卡编辑器** (快捷键 Ctrl+Shift+L)
2. 在弹出的窗口中点击 **新建关卡**
3. 选择保存位置（建议在 `Assets/LevelData/` 文件夹下）
4. 开始编辑关卡配置

### 步骤2：配置关卡基本信息

在关卡编辑器的右侧面板中设置：
- **关卡名称** - 关卡显示名称
- **关卡长度** - 整个关卡的横向长度
- **玩家起始位置** - 玩家出生点
- **终点位置X** - 到达此位置且清完敌人后关卡完成

### 步骤3：添加战斗波次

1. 在左侧面板点击 **+** 添加新波次
2. 设置波次属性：
   - **波次名称** - 用于标识
   - **触发位置X** - 玩家走到此位置时触发战斗
   - **必须清怪才能前进** - 勾选后玩家会被限制在战斗区域内
   - **战斗区域偏移** - 相对于触发位置的左右边界

### 步骤4：配置敌人

1. 选中一个波次
2. 在中间面板点击 **+ 添加敌人** 或 **+ 快速添加**
3. 设置敌人属性：
   - **敌人预制体** - 拖入敌人Prefab
   - **位置** - 相对于触发位置的偏移（X为左右，Y为上下）
   - **延迟** - 波次开始后多少秒生成该敌人

### 步骤5：场景设置

在你的游戏场景中创建以下对象：

#### 1. 创建 BattleWaveManager 对象
```
GameObject: "BattleWaveManager"
└── 组件: BattleWaveManager
    ├── Player Transform: [拖入玩家对象]
    └── Current Level Data: [拖入关卡数据]
```

#### 2. 创建 LevelProgressManager 对象
```
GameObject: "LevelProgressManager"  
└── 组件: LevelProgressManager
    ├── Battle Wave Manager: [自动查找或手动拖入]
    ├── Player Transform: [拖入玩家对象]
    ├── Main Camera: [拖入主相机]
    ├── Camera Fixed Y: 0  (相机固定Y坐标)
    ├── Camera Z: -10  (相机Z坐标)
    └── Current Level Data: [拖入关卡数据]
```
注意：相机采用固定视角模式
- 战斗中相机完全不动
- 战斗结束后，玩家移动到屏幕边缘时相机才会平移
- 相机永远不会上下移动

#### 3. 设置无限滚动背景（单张背景）
```
GameObject: "Background" (你的背景图片对象)
└── 组件: SpriteRenderer (已有的背景精灵)
└── 组件: InfiniteScrollBackground (新添加)
    ├── Background Sprite: [自动获取，或手动拖入]
    ├── Camera Transform: [拖入主相机]
    ├── Parallax Factor: 0.5 (视差系数，0=不动，1=同步)
    └── Fixed Y: 0 (背景固定Y坐标)
```
背景会自动创建左右副本，实现无缝循环滚动。

#### 4. 添加战斗区域可视化（调试用）
```
GameObject: "BattleAreaVisualizer"
└── 组件: BattleAreaVisualizer
    └── Create Boundary Walls: false (默认关闭，不再创建物理墙)
```

---

## 📋 关卡编辑器功能说明

### 左侧面板 - 波次列表
- 显示所有战斗波次
- 点击选中波次进行编辑
- **+** 添加新波次
- **-** 删除选中波次
- **↑↓** 调整波次顺序

### 中间面板 - 波次详情
- 编辑选中波次的详细配置
- 添加/删除/编辑敌人

### 右侧面板 - 关卡预览
- 可视化显示整个关卡布局
- 绿色点 = 起点
- 黄色点 = 终点
- 蓝色竖线 = 波次触发线
- 红色点 = 敌人位置
- 红色区域 = 战斗限制区域

**预览操作：**
- 滚轮缩放
- 中键拖拽移动视图

### 快捷键
- `Ctrl+S` - 保存关卡
- `Delete` - 删除选中项

---

## 🎯 战斗流程说明

1. **关卡开始** - 玩家出现在起始位置
2. **触发波次** - 玩家移动到波次触发位置
3. **战斗开始** - 敌人按配置的延迟时间依次生成
4. **边界限制** - 玩家被限制在相机视野内（不再受战斗区域物理墙限制）
5. **波次完成** - 所有敌人被消灭后，相机解锁
6. **继续前进** - 玩家可以向前移动，触发下一波次
7. **关卡完成** - 所有波次完成且玩家到达终点

---

## 📝 代码使用示例

### 运行时加载关卡
```csharp
// 获取关卡数据
LevelData levelData = Resources.Load<LevelData>("Levels/Level1");

// 加载到管理器
BattleWaveManager.Instance.LoadLevel(levelData);
LevelProgressManager.Instance.StartLevel(levelData);
```

### 监听事件
```csharp
void Start()
{
    BattleWaveManager.Instance.OnWaveStart.AddListener(OnWaveStart);
    BattleWaveManager.Instance.OnWaveComplete.AddListener(OnWaveComplete);
    LevelProgressManager.Instance.OnLevelComplete.AddListener(OnLevelComplete);
}

void OnWaveStart(int waveIndex)
{
    Debug.Log($"波次 {waveIndex + 1} 开始!");
}

void OnWaveComplete(int waveIndex)
{
    Debug.Log($"波次 {waveIndex + 1} 完成!");
}

void OnLevelComplete()
{
    Debug.Log("关卡通关!");
}
```

### 跳过当前波次（调试）
```csharp
// 在BattleWaveManager组件上右键 → "跳过当前波次"
BattleWaveManager.Instance.SkipCurrentWave();
```

---

## 🔧 注意事项

1. **敌人预制体要求**：敌人预制体必须挂载 `Enemy` 组件才能被关卡编辑器识别

2. **相机设置**：`LevelProgressManager` 会接管相机的移动，如果你有自己的相机控制脚本，可能需要禁用或调整

3. **原有Spawner**：新系统会替代原有的 `EnemySpawner`，在使用关卡系统的场景中不需要保留原来的Spawner

4. **边界系统**：战斗时玩家移动会被限制在相机视野内，战斗区域的物理墙限制已默认关闭，敌人可以在战斗区域外自由移动。

5. **存档位置**：建议在 `Assets/` 下创建 `LevelData/` 文件夹专门存放关卡配置文件

---

## 🎨 推荐的文件夹结构

```
Assets/
├── LevelData/           # 关卡配置文件
│   ├── Level1.asset
│   ├── Level2.asset
│   └── ...
├── Prefabs/
│   ├── Enemy.prefab     # 敌人预制体
│   └── MuscleP.prefab
├── Scripts/
│   ├── LevelSystem/     # 关卡系统脚本
│   │   ├── LevelData.cs
│   │   ├── BattleWaveManager.cs
│   │   ├── ParallaxBackground.cs
│   │   ├── LevelProgressManager.cs
│   │   ├── LevelUI.cs
│   │   └── BattleAreaVisualizer.cs
│   └── ...
└── Editor/
    └── LevelEditorWindow.cs
```

---

如有问题，可以：
1. 在Scene视图中查看Gizmos（战斗区域会显示为黄色线框）
2. 查看Console中的Debug日志
3. 使用关卡编辑器的"验证"功能检查配置
