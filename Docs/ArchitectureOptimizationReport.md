# Harma 2D 横版过关项目架构整改报告

更新日期：2026-08-18

## 1. 已确认的玩法边界

- 游戏地面平面使用 **X/Y**：X 为横向推进，Y 为纵深移动。
- 跳跃高度不是 Transform/Rigidbody2D 的 Y，而是独立的视觉与判定高度。
- 当前玩家攻击仅包含跳跃踩踏；地面攻击、连招及攻击输入不属于当前版本。
- `Bridge_PV` 是保留的测试关卡，不是正式构建关卡，也不是废弃资源。

## 2. 整改后的核心架构

### 2.1 玩家坐标与跳跃

- `Rigidbody2D.position` 始终表示地面 X/Y 坐标。
- `PlayerMovement.JumpHeight` 独立表示离地高度。
- Spine 骨骼根节点承担跳跃视觉偏移，玩家根对象、碰撞体、阴影、相机跟随和 YSort 保持在地面坐标。
- 踩踏判定同时要求：地面平面接近、玩家正在下降、跳跃高度向下穿过敌人的可踩高度。
- 剧情移动、击退、受伤落地、敌人近战和投射物纵深判定统一使用地面 Y。

### 2.2 战斗与敌人解耦

- 新增独立战斗契约程序集 `Harma.Combat.Contracts`。
- 敌人目标注入、攻击状态、受击反馈和踩踏目标均通过接口通信。
- 清除了 AI/Spawner 对具体敌人脚本的强耦合、反射调用和 `SendMessage`。
- `EnemyAttackCollider` 通过父级组件和接口获取攻击状态，降低预制体层级变化风险。

### 2.3 生命周期与场景职责

- `LevelManager` 和 `LevelCameraController` 增加静态状态清理，避免退出 Play Mode 或切场景后残留单例/锁屏状态。
- 相机锁引入所有权，防止多个战斗区互相错误解锁。
- `Bridge_PV` 移至 `Assets/Scenes/Tests/`，GUID 保持不变，并从 Build Settings 排除。
- 构建场景生成逻辑明确忽略测试场景目录。

### 2.4 程序集与大类拆分

- GameFlow 核心服务移至 `Assets/Scripts/GameFlow/Core/`，形成独立程序集。
- `LevelData` 拆分为模型、查询、迁移、验证等 partial 文件。
- `StoryManager` 拆分为数据、输入、生命周期、播放、结果处理。
- `StoryUI` 拆分为可见性、内容、头像和过渡动画。
- 保留原 MonoScript GUID、类名和序列化字段，避免场景/预制体引用丢失。

## 3. 已清理的冗余内容

- 从玩家正式预制体和 4 个旧/测试场景移除共 7 个 `PlayerAttack` 组件。
- 删除 `PlayerAttack.cs` 及 Attack Input Action/Bindings。
- `PlayerHP` 与 `Obstacle` 不再依赖已取消的地面攻击系统。
- 从正式敌人预制体移除重复的 `EnemySimpleAI2D`，避免双 AI 同时驱动。
- 从 `Bridge`、`Main_Level`、`Bridge_PV` 清除 9 个已废弃旧关卡流程的 Missing Script 组件；对应源码在 Git 提交 `220034f` 中已主动删除。
- `StoryTrigger.LevelStart` 从已删除 Manager 的反射订阅迁移到 `LevelSceneBuilder.OnLevelReady`，旧测试场景使用下一帧兼容回退；未使用的旧波次触发类型会明确提示迁移。
- 移除无任何项目资源依赖的直接包：
  - `com.unity.multiplayer.center`
  - `com.unity.timeline`
  - `com.unity.visualscripting`
- 经负责人明确授权，删除零外部依赖的 `Assets/Spine Examples` 官方示例目录（618 个文件）。
- 保留可能属于制作工作流的 Visual Studio、版本协作、Aseprite、PSD、2D 与 Spine 运行库。

## 4. 质量与兼容性修复

- UI 自定义 Shader 补齐 Unity `MaskableGraphic` 所需的标准 Stencil 属性。
- 保留 `BlockColor`/`StripeOverlay` 现有 bit 0/bit 1 协议，同时消除运行时材质警告。
- `EnemySimpleAI2D` 增加玩家缺失保护。
- 移动、跳跃和反弹持续时间增加安全下限，避免除零和异常状态。
- 75 个高频纯调试输出改为 `HARMA_VERBOSE_LOGS` 条件编译；默认构建不生成字符串、不调用 Console。
- 移除剧情逐字动画中两个仅用于输出日志的 DOTween 回调，保留实际字符回调与动画时序。

## 5. 自动化验证

- EditMode：22/22 通过。
- PlayMode：1/1 通过。
- PlayMode 回归覆盖“跳跃过程中仍可沿地面 Y 纵深移动，跳跃高度独立且最终归零”。
- `NewLevel_test` 真实场景冒烟测试通过：玩家正常生成，已无 `PlayerAttack`，完整控制台 Log/Warning/Error/Exception 均为 0。
- 对 `Assets/Scenes` 下 11 个场景及项目 12 个 Prefab 执行缺失脚本审计，Missing Script 数量为 0。
- Unity 编辑器最终恢复到未修改的 `GameClear` 场景。

## 6. Spine 示例清理结果

`Assets/Spine Examples` 原包含 618 个 Spine 官方示例文件。审计和执行结果如下：

- 构建场景和正式玩家/敌人预制体依赖数：0。
- 目录外所有项目资源的反向依赖数：0。
- 项目负责人已明确授权，目录及其 `.meta` 已通过 Unity AssetDatabase 原子删除。
- 删除后 Unity 域重载、EditMode、PlayMode 和 `NewLevel_test` 冒烟测试全部通过。
- `Assets/Spine` 运行库与 `Assets/Spine Skeletons` 项目角色资源不在删除范围内。

## 7. Spine 色彩空间决策与剩余资源工作

`Bridge_PV` 编辑器验证确认 Kaho、PunkP、FatP、MuscleP 使用真实 PMA 图集，而项目当前采用 Linear 色彩空间。Spine 官方运行库明确提示该组合不受支持。

- 项目负责人已确认保持 Linear Color Space，不切换 Gamma；`ProjectSettings` 的色彩空间设置保持不变。
- 正式构建依赖闭包同样受影响：3 个材质、3 张图集 PNG，覆盖 Kaho 与 Pmacho 系列，并非只影响测试关卡。
- 仓库内只有已导出的 JSON、atlas 和打包 PNG，没有任何 `.spine` 原始工程，因此无法在当前工作区无损重新导出 Straight Alpha 图集。
- 不能只切换材质的 `Straight Alpha Texture`，因为当前 PNG 的确是 PMA 数据，强行切换会产生颜色和边缘错误。
- 已增加 `Tools > Harma > Validate Spine Alpha Compatibility` 校验器：仅扫描启用的正式构建场景依赖，并在构建前集中报告仍使用 PMA 的 Spine 材质。
- 已整理 `Docs/SpineStraightAlphaReexportChecklist.md`，列出 Kaho/Pmacho 的精确资源、Spine 重导参数、Unity 替换步骤和验收标准。
- 剩余外部依赖：取得 Kaho 与 Pmacho 的原始 `.spine` 工程，从 Spine 关闭 Premultiply Alpha 重新导出，并按清单替换和验证。完成前保留明确警告，不对 PNG 做有损转换，也不伪改材质标记。

## 8. 明确保留和未改动内容

- `Assets/_Recovery` 全部保留，未删除、未重写。
- `Bridge_PV` 保留为测试关卡。
- 不新增地面攻击、连招或攻击输入。
- 不擅自移除可能属于美术/程序制作工作流的导入器、IDE 和协作包。

## 9. 后续迭代原则

- 新增攻击方式时扩展战斗契约，不把具体 AI 类型重新写回 Spawner、Collider 或玩家脚本。
- 所有“距离”必须明确区分地面平面距离与离地高度。
- 正式关卡进入 Build Settings；实验/验证场景放在 `Assets/Scenes/Tests/`。
- 大型数据和 UI 控制类继续按职责拆分，避免重新形成千行级单文件。
