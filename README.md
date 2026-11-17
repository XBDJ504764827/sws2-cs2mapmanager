# SWS2 CS2 Map Manager

SwiftlyS2 的共享地图目录与换图服务。管理员选图、玩家 `!yd` 预定和无人预定时的 RTV 选图读取同一份地图列表；倒计时、工坊下载、暂停、取消和最终换图由本插件统一处理。

## 与其他 SWS2 插件搭配

| 插件 | 职责 | 与本插件的关系 |
| --- | --- | --- |
| [sws2-cs2menus](https://github.com/XBDJ504764827/sws2-cs2menus) | 共享菜单、分页、导航、地图选择 HUD | 为 admin / RTV 提供统一界面 |
| [sws2-cs2mapmanager](https://github.com/XBDJ504764827/sws2-cs2mapmanager) | 统一地图目录、评分、工坊下载和可取消换图 | 本插件 |
| [sws2-cs2admin](https://github.com/XBDJ504764827/sws2-cs2admin) | 管理员权限、玩家管理、封禁、换图控制 | 使用本插件浏览地图、换图、暂停和取消 |
| [sws2-cs2rockthevote](https://github.com/XBDJ504764827/sws2-cs2rockthevote) | 玩家预定地图、RTV 发起投票与选图 | 使用本插件读取地图及执行换图 |
| [sws2-flashlight](https://github.com/XBDJ504764827/sws2-flashlight) | 随视角移动的手电筒 | 可选，换图后清理旧手电筒 |
| [sws2-nightvision](https://github.com/XBDJ504764827/sws2-nightvision) | 玩家独立夜视与亮度偏好 | 可选，按夜视设置在出生时恢复 |

常用组合：玩家换图安装 **menus + mapmanager + rockthevote**；需要管理面板时再加 **admin**；手电筒和夜视按需加入。工坊地图自动下载和 HUD 工坊资源自动挂载还需要服务器安装 **MultiAddonManager**。本套件不提供 KZ 计时、检查点或 CKZ/VNL 模式本身。

## 安装与首次配置

1. 安装 SwiftlyS2；工坊地图自动下载还需 MultiAddonManager。
2. 从本仓库 Releases 或 CI artifact 下载构建包，将完整 `sws2-cs2mapmanager/` 解压到 `csgo/addons/swiftlys2/plugins/`，保留主 DLL、合约 DLL、`resources/exports/` 和模板。
3. 重启服务器，或在服务器控制台执行 `sw plugins load sws2-cs2mapmanager`。手动组合加载时，先加载本插件和 menus，再加载 admin / rockthevote。
4. 编辑首次生成的 `csgo/addons/swiftlys2/configs/plugins/sws2-cs2mapmanager/config.jsonc`。本插件不提供玩家选图命令；安装 admin 使用 `!admin`，或安装 rockthevote 使用 `!yd` / `!rtv`。

## 地图目录配置

下面是完整配置结构。工坊 ID 来自对应地图的 Steam 工坊页面；示例评分保持 `null`，请填真实数据。

```jsonc
{
  "sws2-cs2mapmanager": {
    "Debug": false,
    "Maps": {
      "WorkshopDownloadCommand": "mm_download_addon",
      "WorkshopDownloadTimeoutSeconds": 600,
      "Available": {
        "kz_func_detail": {
          "WorkshopId": "3798986356",
          "CKZ": {
            "NUB": null,
            "PRO": null
          },
          "VNL": {
            "NUB": null,
            "PRO": null
          }
        },
        "kz_sxb2_cxz": {
          "WorkshopId": "3083714192",
          "CKZ": {
            "NUB": null,
            "PRO": null
          },
          "VNL": {
            "NUB": null,
            "PRO": null
          }
        },
        "kz_sc_ruins": {
          "WorkshopId": "3767757448",
          "CKZ": {
            "NUB": null,
            "PRO": null
          },
          "VNL": {
            "NUB": null,
            "PRO": null
          }
        }
      }
    }
  }
}
```

`Maps.Available` 是唯一目录，没有目录或内容无效时不会自动生成备用地图。对象属性顺序决定列表展示顺序；**无人预定时的 RTV 由玩家选图，列表顺序不决定胜出地图**。修改配置支持热重载，重开菜单查看新数据。

### 地图条目的写法

可用以下对象替换 `Maps.Available`。简写与完整对象可以混用：

```jsonc
{
  "kz_func_detail": "3798986356",
  "de_dust2": "local:de_dust2",
  "kz_angina": {
    "WorkshopId": "3636272568",
    "CKZ": { "NUB": null, "PRO": null },
    "VNL": { "NUB": null, "PRO": null }
  },
  "本地 Mirage": {
    "MapName": "de_mirage",
    "CKZ": { "NUB": null, "PRO": null },
    "VNL": { "NUB": null, "PRO": null }
  }
}
```

| 字段 | 用法 |
| --- | --- |
| 对象键 | 菜单显示名称；完整对象可用 `Name` 覆盖 |
| `WorkshopId` | 工坊地图数字 ID，不是 HUD addon ID |
| `MapName` | 已安装的本地地图名；与 `WorkshopId` 二选一 |
| `CKZ.NUB` / `CKZ.PRO` | CKZ 存点 / 裸跳的独立评分 |
| `VNL.NUB` / `VNL.PRO` | VNL 存点 / 裸跳的独立评分 |

评分接受正整数或对应数字字符串；缺失、`null`、占位字符串和无效值显示未知，不跨模式或跑法补值。旧的 `"CKZ": 2` / `"VNL": 3` 只能表示旧版模式评分，不能确定 NUB / PRO，新菜单不会用它替代四类评分。此配置不会安装或切换 CKZ/VNL 游戏模式。

仍兼容由 `Name`、`MapName`、`WorkshopId`、`CKZ`、`VNL` 构成的旧数组。JSONC 字段之间使用逗号，四类评分必须放进对应地图对象中。另有 [地图目录填写模板](resources/templates/map-catalog.example.jsonc)，可复制其中的 `Available`，保留自己服务器的其他设置。

## 与 admin 和 RTV 的完整流程

1. 在本插件目录配置地图后，`!admin` → 地图管理、`!yd` 和 RTV 选图面板使用相同地图名称、稳定 ID 和四类评分。
2. admin 选图确认后由本插件执行 3 秒倒计时；有预定的 RTV 达到门槛后默认 5 秒倒计时；无预定的 RTV 选图结束后以 0 秒延迟请求换图。
3. 本地或已安装地图在到期后执行换图。缺失工坊地图在到期后才发起下载，下载完成后自动切换。
4. 管理员可用 `!cancelmap` 取消本次、`!pausemaps` 暂停、`!resumemaps` 恢复。需要 admin 的 `sws2.admin.map.control`；无 admin 时其他消费者仍可调用共享 API。

取消会清空 RTV 同意票及活跃选图面板，即使尚未创建换图倒计时也会通知 RTV。暂停还会拒绝新的托管换图。恢复不复活旧任务，玩家需要重新投票；RTV 保留预定地图。暂停在地图卸载、插件卸载或重启后重置。

一次只接受一个托管换图流程。取消能阻止倒计时或下载完成后的自动切换，不能撤回已经发给引擎的命令；Steam 下载本身也可能继续完成。原始控制台 `changelevel` / `host_workshop_map` 或绕过本服务的插件不在控制范围内。

## 工坊下载与 HUD 资源的区别

本插件使用 `mm_download_addon <地图ID>` 请求 MultiAddonManager 下载，检查服务器工坊文件直至完成，再执行 `host_workshop_map <地图ID>`。MAM 跟踪当前工坊地图并将其提供给连接/重连的客户端。本插件不会把目录里的每张地图都加到 `mm_extra_addons`。

| 设置 | 默认值 | 说明 |
| --- | --- | --- |
| `Maps.WorkshopDownloadCommand` | `mm_download_addon` | MAM 提供的服务器下载命令；空字符串禁用自动下载 |
| `Maps.WorkshopDownloadTimeoutSeconds` | `600` | 等待安装完成的超时秒数 |
| `Debug` | `false` | 调试日志 |

服务器需登录 Steam 且能访问工坊。禁用自动下载时，先在服务器安装目标工坊地图。下载失败或超时会报告错误，不会把未就绪地图直接交给 `host_workshop_map`。

**地图截图属于 menus 的 HUD addon，不由地图下载生成。** 没截图不影响地图可用性；要添加图片，使用 menus 的 `tools/build_map_thumbnails.py`，然后通过 Workshop Tools 编译、更新 HUD addon。只改地图评分或显示名称不需要更新 addon。

## 常见问题

| 问题 | 排查方法 |
| --- | --- |
| admin / `!yd` 都没有地图 | 检查外层插件节点、`Maps.Available`、JSONC 格式和加载日志 |
| 某地图不在列表 | 检查工坊 ID / 本地图名是否有效，是否错误同时填写二者 |
| 四类难度全是未知 | 填嵌套 `CKZ` / `VNL` 的 `NUB` / `PRO`，然后重开菜单 |
| 工坊地图一直等待 | 检查 MAM 是否加载、`mm_download_addon` 是否存在、Steam 下载和超时日志 |
| 所有托管换图被拒绝 | 在 admin 地图管理查看暂停/待处理状态，必要时取消或恢复 |
| 取消后仍看到下载流量 | 取消的是后续换图，底层 Steam 下载可能继续 |

## 共享 API

导出合约位于 `resources/exports/Sws2Cs2MapManager.Contracts.dll`。消费者编译时引用合约并设置 `Private=false`，在 `UseSharedInterface` 中通过 `IMapManagerService.InterfaceKey` 解析 `IMapManagerService`；部署时保持共享合约版本一致。

| API | 用途 |
| --- | --- |
| `GetMaps()` | 按配置顺序读取 `MapInfo`，包含稳定 `Id` 和四类可空评分 |
| `ScheduleChange(mapId, delaySeconds, source)` | 在游戏线程创建可取消换图，延迟限制为 0–300 秒 |
| `RequestChange(mapId)` | 立即请求；同样遵守暂停和忙碌状态 |
| `GetChangeState()` | 查询暂停人、待切地图、来源、到期时间和下载状态 |
| `CancelPendingChange()` | 取消倒计时及下载后的自动切换，通知消费者清理投票 |
| `SetChangesPaused(paused, actor)` | 暂停或恢复接受托管换图 |
| `MapChangeProgressed` | 倒计时、下载、发出换图、失败、取消事件 |
| `MapChangeControlChanged` | 取消/暂停/恢复控制变更通知 |

`MapInfo.CkzNubTier`、`CkzProTier`、`VnlNubTier`、`VnlProTier` 是独立可空整数。旧 `CkzTier` / `VnlTier` 仅保留旧数据；原四参数构造函数仍兼容。调用和游戏相关处理在游戏线程进行。

## 源码构建、检查与升级

```sh
dotnet restore sws2-cs2mapmanager.csproj
dotnet build sws2-cs2mapmanager.csproj -c Release -warnaserror
dotnet publish sws2-cs2mapmanager.csproj -c Release --no-restore
dotnet run --project tests/CatalogChecks/CatalogChecks.csproj -c Release
dotnet run --project tests/ChangeChecks/ChangeChecks.csproj -c Release
```

安装包：`build/sws2-cs2mapmanager.zip`。目录检查覆盖四类评分、空值、简写/完整对象与旧数组；换图检查覆盖取消、过期回调、下载完成、暂停恢复、地图卸载、配置删除及消费者/调度器失败。

更新共享合约时，配套更新 menus、mapmanager、admin 和 rockthevote 并重启服务器；保留现有配置。各仓库发布包或 CI 测试包应使用兼容的一组版本。
