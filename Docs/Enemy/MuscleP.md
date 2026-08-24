# MuscleP 敌人设计与调参文档

> 本文档对应 `MuscleP.prefab` 与 `MuscleP_AI_Movement.cs` 的当前实现。表中的默认值以预制体实际序列化配置为准，而不是脚本字段声明值。

## 1. 角色定位

MuscleP 是一个以近中距离压迫和横向冲刺为核心的近战敌人。它会在玩家周围调整距离和 Y 轴通道，满足条件后蓄力并沿水平方向高速穿过玩家。

新增远距离舞蹈后，MuscleP 在双方明显脱离时有机会暂时停止追击并进行挑衅演出；玩家靠近、受击或演出超时后会立即恢复战斗逻辑。

### 基础特点

- 主要伤害方式为横向冲刺，只有冲刺阶段开启攻击判定。
- 攻击前要求进入 7 单位范围并与玩家基本处于同一 Y 轴通道。
- 距离小于 3 单位时倾向于攻击或后退，距离大于 8 单位时倾向于接近。
- 15 秒未完成攻击时进入强制攻击流程。
- 屏幕外会优先回到玩家附近，不会攻击或跳舞。
- 生命值当前为 1，受击会中断移动、冲刺和舞蹈。

## 2. 动作表

| 动作 | Spine 动画 | 是否循环 | 位移/效果 | 结束条件 |
| --- | --- | --- | --- | --- |
| 待机 | `idle` | 是 | 速度归零 | 随机等待结束或更高优先级状态接管 |
| 行走 | `walk` | 是 | 按 `speed` 移动 | 抵达目标点、受击或状态切换 |
| 接近 | `walk` | 是 | 调整距离并优先对齐 Y 轴 | 抵达本轮接近目标点 |
| 游走/退避 | `walk` | 是 | 绕行玩家或向玩家反方向移动 | 抵达本轮游走目标点 |
| 蓄力 | `attack` | 否 | 原地、面向玩家 | `chargeTime` 结束 |
| 冲刺 | 延续 `attack` | 否 | 固定 Y 轴高速横向穿过玩家 | 达到冲刺距离、边界或受击 |
| 舞蹈开场 | `dance03` | 否 | 原地、锁定朝向 | 动画完成或舞蹈被中断 |
| 舞蹈过渡 | `dance to dance02` | 否 | 原地 | 动画完成或舞蹈被中断 |
| 舞蹈循环 | `dance02` | 是 | 原地 | 玩家靠近、超时或其他中断 |
| 受击 | 当前未配置专用动画 | - | 由 `Enemy` 处理击退，AI 停止主动移动 | 受击状态解除 |
| 死亡 | `die` | 否 | 停止正常行为 | 死亡流程完成 |

## 3. 行为决策逻辑

### 3.1 决策优先级

MuscleP 每轮状态决策按以下顺序处理：

1. 受击或死亡中断
2. 入场移动
3. 屏幕外回归
4. 正在执行的冲刺攻击
5. 正在执行的舞蹈
6. 远距离舞蹈判定
7. 15 秒未攻击的强制接近/攻击
8. 普通接近、保持距离、游走或待机

状态协程执行期间不会每帧重新抽取随机结果。舞蹈概率只在一次完整状态结束、AI 重新做出决策时判定一次。

### 3.2 普通状态选择

- Y 轴差超过 `maxYAxisOffset` 时，优先接近并调整通道。
- 与玩家同通道、距离不超过 `attackRange` 且允许攻击时，根据距离和 `attackDesire` 判定攻击。
- 距离小于 `minKeepDistance` 时，优先考虑近身攻击；未攻击则游走退避。
- 距离大于 `maxKeepDistance` 时执行接近。
- 攻击结束后有 40% 概率短暂停顿。
- 普通随机分配为 20% 游走、10% 待机、70% 接近；待机不会连续出现。

攻击基础概率会随距离变化：

| 距离情况 | 基础攻击概率 |
| --- | ---: |
| 不超过 `stopDistance` | 80% |
| 不超过攻击范围的一半 | 60% |
| 其余攻击范围内位置 | 45% |

最终概率还会乘以 `attackDesire / 50`，并限制在 0～100%。

### 3.3 强制攻击

- 从上次攻击完成开始计时。
- 达到 `forceAttackTime` 后，若距离或 Y 轴不满足攻击条件，则强制接近。
- 同通道且进入攻击范围后立即选择冲刺攻击。
- 舞蹈判定优先于强制攻击，但舞蹈不重置攻击计时；舞蹈结束后冷却会阻止再次跳舞，已到期的强制攻击会立即接管。

## 4. 冲刺攻击流程

1. 进入攻击状态后停止移动并锁定朝向。
2. 播放一次 `attack`，原地蓄力 `chargeTime`。
3. 锁定水平冲刺方向和当前 Y 坐标。
4. 冲刺距离为“与玩家的水平距离 + `playerBodyWidth × 2`”。
5. 以 `dashSpeed` 沿 X 轴移动，冲刺期间 `IsAttackActive` 为真。
6. 到达目标距离、碰到水平边界或受击时停止冲刺。
7. 原地恢复 `postAttackDelay` 后结束攻击状态。

玩家在蓄力完成后移动不会改变本轮已锁定的水平冲刺方向。冲刺不会主动追踪 Y 轴。

## 5. 远距离舞蹈逻辑

### 5.1 触发条件

以下条件必须同时满足：

- `enableDance` 已开启。
- MuscleP 在屏幕内，且不在入场、受击、攻击或舞蹈状态。
- 玩家目标有效。
- 双方距离不小于 `danceStartDistance`，默认 10 单位。
- 初始或上一次舞蹈结束后的冷却已完成。
- 本次状态决策的概率判定成功，默认概率 30%。

### 5.2 动画与持续时间

进入舞蹈时会归零刚体速度、面向玩家并锁定朝向，然后使用 Spine 动画队列播放：

```text
dance03（一次）
    → dance to dance02（一次）
        → dance02（循环）
```

单次舞蹈最多持续 `danceMaxDuration`，默认 6 秒。即使动画资源缺失，AI 状态和退出逻辑仍能正常完成，不会阻塞战斗。

### 5.3 退出和中断

以下任一情况会结束舞蹈并清除尚未播放的动画队列：

- 玩家距离不超过 `danceExitDistance`，默认 8 单位。
- 舞蹈达到最长 6 秒。
- MuscleP 受击或死亡。
- 玩家目标失效。
- 相机移动导致 MuscleP 离开屏幕。
- 对象或 AI 组件被禁用。

正常结束后切回待机动画，随后重新进行状态决策。所有提前结束都会启动完整舞蹈冷却。8～10 单位的进入/退出差形成滞回区间，避免在距离边缘反复切换。

舞蹈不属于攻击状态，不会开启伤害判定，也不会修改 `lastAttackTime`。

## 6. MuscleP_AI_Movement 可调参数

### 6.1 基础移动

| Inspector 参数 | 当前值 | 作用 |
| --- | ---: | --- |
| `speed` | 1.2 | 普通移动速度 |
| `stopDistance` | 1.5 | 极近距离判断和部分站位逻辑参考值 |
| `startDelay` | 0 秒 | 获得目标后的初始行动延迟 |
| `minMoveTime` | 0.6 秒 | 预留移动时长参数；当前目标点移动逻辑未直接使用 |
| `maxMoveTime` | 1.8 秒 | 预留移动时长参数；当前目标点移动逻辑未直接使用 |
| `minWaitTime` | 0.5 秒 | 待机最短时间 |
| `maxWaitTime` | 1.5 秒 | 待机最长时间 |

### 6.2 距离与游走

| Inspector 参数 | 当前值 | 作用 |
| --- | ---: | --- |
| `minKeepDistance` | 3 | 小于该距离时倾向攻击或后退 |
| `maxKeepDistance` | 8 | 大于该距离时执行接近 |
| `minWanderDistance` | 2 | 单次游走/退避的最小距离 |
| `maxWanderDistance` | 4 | 单次游走/退避的最大距离 |
| `maxYAxisOffset` | 0.5 | 超过该 Y 轴差时强制对齐 |

### 6.3 冲刺攻击

| Inspector 参数 | 当前值 | 作用 |
| --- | ---: | --- |
| `attackDesire` | 70.2% | 普通决策中的攻击倾向 |
| `attackRange` | 7 | 允许开始攻击的最大距离 |
| `yAxisTolerance` | 0.5 | 攻击同通道容差及攻击判定通道宽度 |
| `forceAttackTime` | 15 秒 | 多久未攻击后进入强制攻击流程 |
| `chargeTime` | 0.5 秒 | 冲刺前摇时间 |
| `dashSpeed` | 10 | 水平冲刺速度 |
| `playerBodyWidth` | 1 | 计算穿过玩家后的追加冲刺距离 |
| `postAttackDelay` | 0.5 秒 | 冲刺结束后的恢复时间 |

### 6.4 远距离舞蹈

| Inspector 参数 | 当前值 | 作用 |
| --- | ---: | --- |
| `enableDance` | 开启 | 是否启用 MuscleP 的远距离舞蹈状态 |
| `danceStartDistance` | 10 | 新进入舞蹈所需的最小距离 |
| `danceExitDistance` | 8 | 玩家靠近后退出舞蹈的距离 |
| `danceChance` | 30% | 冷却完成后每次状态决策的触发概率 |
| `danceCooldown` | 8 秒 | 舞蹈结束到下次允许判定的间隔；同时也是初始冷却 |
| `danceMaxDuration` | 6 秒 | 单次舞蹈最长持续时间 |
| `danceIntroAnimation` | `dance03` | 舞蹈开场动画 |
| `danceTransitionAnimation` | `dance to dance02` | 开场到循环动作的过渡动画 |
| `danceLoopAnimation` | `dance02` | 舞蹈循环动画 |

### 6.5 常规动画名称

| Inspector 参数 | 当前值 |
| --- | --- |
| `idleAnimName` | `idle` |
| `walkAnimName` | `walk` |
| `attackAnimName` | `attack` |

## 7. 边界、入场和屏外行为

- X 轴逻辑范围当前为 -1000～1000，等同于常规关卡中不主动限制追击距离。
- Y 轴上下界从 `LevelManager` 读取；缺少管理器时使用 -5～5。
- 入场速度为普通速度的 1.5 倍，即当前约 1.8。
- 屏幕外回归速度为普通速度的 1.2 倍，即当前约 1.44。
- 入场和屏外回归均高于舞蹈、强制攻击和普通状态。

## 8. 常用调参方案

### 让舞蹈更常见

- 提高 `danceChance`。
- 缩短 `danceCooldown`。
- 降低 `danceStartDistance`，但应保持其大于 `maxKeepDistance`，否则会频繁打断正常站位。

### 让舞蹈更像偶发彩蛋

- 将 `danceChance` 降到 10%～20%。
- 将 `danceCooldown` 提高到 10～15 秒。
- 将 `danceStartDistance` 提高到 12 单位以上。

### 提高冲刺压力

- 提高 `attackDesire` 或缩短 `forceAttackTime`。
- 缩短 `chargeTime` 会降低预警时间，应谨慎调整。
- 提高 `dashSpeed` 会提高命中压力，但也会缩短伤害窗口的视觉停留时间。

### 提高玩家反应空间

- 延长 `chargeTime`。
- 降低 `dashSpeed`。
- 降低 `attackDesire` 或延长 `forceAttackTime`。

## 9. 中断与注意事项

- 受击会立即关闭冲刺伤害窗口并中断舞蹈。
- 舞蹈期间持续归零刚体速度，确保外观上保持原地。
- 舞蹈进入时锁定朝向，玩家绕到另一侧不会导致动画左右抖动。
- 玩家进入 8 单位内即可打断舞蹈，不要求与 MuscleP 处于同一 Y 轴通道。
- `danceExitDistance` 会被限制为不大于 `danceStartDistance`；概率限制为 0～100%，所有距离与时间参数不得为负。
- `Enemy.prefab` 虽然也挂载同一 AI 脚本，但 `enableDance` 默认关闭；只有 `MuscleP.prefab` 默认开启。

## 10. 相关文件与测试

- AI：`Assets/Scripts/MuscleP_AI_Movement.cs`
- 预制体：`Assets/Prefabs/MuscleP.prefab`
- Spine 舞蹈参考组件：`Assets/Scripts/SpineDanceSequence.cs`，本行为不会直接挂载该组件
- PlayMode 测试：`Assets/Tests/PlayMode/MusclePCombatPlayModeTests.cs`
- 本文档：`Docs/Enemy/MuscleP.md`

PlayMode 测试覆盖初始冷却、距离与概率判定、强制攻击优先关系、原地舞蹈、最长持续时间、玩家靠近中断、受击中断、屏外禁止和动画名称配置。
