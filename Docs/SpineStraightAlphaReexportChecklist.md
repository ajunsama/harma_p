# Spine Straight Alpha 重导与替换清单

## 背景与边界

项目决定继续使用 **Linear Color Space**。当前正式构建依赖中的 Kaho 与 Pmacho 图集是 Premultiplied Alpha（PMA）数据，不能只在 Unity 材质中勾选 `Straight Alpha Texture`；这样会让材质解释方式与像素数据不一致，产生暗边、亮边或颜色错误。

仓库中没有原始 `.spine` 工程，因此不能在当前工作区无损完成纹理转换。不要对现有 PNG 做有损“反预乘”后冒充正式重导资源。

## 当前需要美术重导的资源

### Kaho

- `Assets/Spine Skeletons/kaho/kaho k01.json`
- `Assets/Spine Skeletons/kaho/kaho k01.atlas.txt`
- `Assets/Spine Skeletons/kaho/kaho k01.png`
- `Assets/Spine Skeletons/kaho/kaho k012.png`
- `Assets/Spine Skeletons/kaho/kaho k01_kaho k01.mat`
- `Assets/Spine Skeletons/kaho/kaho k01_kaho k012.mat`

### Pmacho（PunkP、FatP、MuscleP 共用来源）

- `Assets/Spine Skeletons/Pmacho/Pmacho.json`
- `Assets/Spine Skeletons/Pmacho/Pmacho.atlas.txt`
- `Assets/Spine Skeletons/Pmacho/Pmacho.png`
- `Assets/Spine Skeletons/Pmacho/Pmacho_Material.mat`

## Spine 导出要求

1. 使用与现有 Spine Runtime 兼容的 Spine Editor 版本打开原始 `.spine` 工程。
2. 导出 JSON 与 Texture Atlas 时关闭 **Premultiply Alpha**，输出 Straight Alpha 图集。
3. 尽量保持 skeleton 名称、skin/slot/attachment 名称、atlas region 名称以及页文件名不变，避免破坏现有动画和 Prefab 引用。
4. 若新版导出改变 atlas 页数、文件名或 region 布局，JSON、`.atlas.txt` 和全部 PNG 必须作为同一批次一起替换。
5. 保留原始重导文件，不要使用截图、图像压缩网站或对现有 PMA PNG 进行反预乘来生成正式资源。

## Unity 替换与验证

1. 替换对应 JSON、`.atlas.txt` 和 PNG，让 Unity 完成重新导入。
2. 检查 Straight Alpha PNG 的 Texture Importer：启用 `Alpha Is Transparency`，不要再执行 PMA 纹理处理。
3. 对上述三个正式构建依赖材质启用 `Straight Alpha Texture`：`_StraightAlphaInput = 1`，并确认 `_STRAIGHT_ALPHA_INPUT` keyword 已启用。
4. 执行菜单 `Tools > Harma > Validate Spine Alpha Compatibility`；正式构建依赖的 incompatible 数量必须为 0。
5. 运行全部 EditMode 和 PlayMode 测试。
6. 分别打开 `NewLevel_test` 与 `Assets/Scenes/Tests/Bridge_PV.unity`，检查 Kaho、PunkP、FatP、MuscleP：
   - 控制台不再出现 PMA/Linear 警告；
   - 深色背景与浅色背景下均无黑边、白边或颜色溢出；
   - 动画、换肤、层级遮挡与材质混合正常。

## 完成标准

- 项目仍为 Linear Color Space。
- 构建依赖扫描器报告 0 个不兼容材质。
- 场景运行无 Spine PMA/Linear 警告，角色边缘视觉验收通过。
