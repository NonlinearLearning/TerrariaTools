using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.GameContent.Achievements;
using Terraria.GameContent.Creative;
using Terraria.GameContent.Events;
using Terraria.GameContent.Golf;
using Terraria.GameContent.Tile_Entities;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.Net;
using Terraria.PacketAnalysis;
using Terraria.Net.Sockets;
using Terraria.Testing;
using Terraria.UI;

namespace Terraria;

public class MessageBuffer
{
    public static bool UseComprehensiveAnalysis = true;
    public const int readBufferMax = 131070;
    public const int writeBufferMax = 131070;
    public bool broadcast;
    public byte[] readBuffer = new byte[131070];
    public byte[] writeBuffer = new byte[131070];
    public bool writeLocked;
    public int messageLength;
    public int totalData;
    public int whoAmI;
    public int spamCount;
    public int maxSpam;
    public bool checkBytes;
    public MemoryStream readerStream;
    public MemoryStream writerStream;
    public BinaryReader reader;
    public BinaryWriter writer;
    public PacketHistory History = new PacketHistory();
    private float[] _temporaryProjectileAI = new float[Projectile.maxAI];
    private float[] _temporaryNPCAI = new float[NPC.maxAI];
    public int RemainingReadBufferLength => readBuffer.Length - totalData;

    public static event TileChangeReceivedEvent OnTileChangeReceived;
    public void Reset()
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.Reset"))
        {
            Array.Clear(readBuffer, 0, readBuffer.Length);
            Array.Clear(writeBuffer, 0, writeBuffer.Length);
            writeLocked = false;
            messageLength = 0;
            totalData = 0;
            spamCount = 0;
            broadcast = false;
            checkBytes = false;
            ResetReader();
            ResetWriter();
        }
    }

    public void ResetReader()
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.ResetReader"))
        {
            if (readerStream != null)
            {
                readerStream.Close();
            }

            readerStream = new MemoryStream(readBuffer);
            reader = new BinaryReader(readerStream);
        }
    }

    public void ResetWriter()
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.ResetWriter"))
        {
            if (writerStream != null)
            {
                writerStream.Close();
            }

            writerStream = new MemoryStream(writeBuffer);
            writer = new BinaryWriter(writerStream);
        }
    }

    private float[] ReUseTemporaryProjectileAI()
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.ReUseTemporaryProjectileAI"))
        {
            for (int i = 0; i < _temporaryProjectileAI.Length; i++)
            {
                _temporaryProjectileAI[i] = 0f;
            }

            return _temporaryProjectileAI;
        }
    }

    private float[] ReUseTemporaryNPCAI()
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.ReUseTemporaryNPCAI"))
        {
            for (int i = 0; i < _temporaryNPCAI.Length; i++)
            {
                _temporaryNPCAI[i] = 0f;
            }

            return _temporaryNPCAI;
        }
    }

    // 处理接收到的网络数据
    public void GetData(int start, int length, out int messageType)
    {
        // 使用调用跟踪器记录当前方法调用
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.GetData"))
        {
            // 如果是服务器端（whoAmI < 256 表示具体的客户端连接索引）
            if (whoAmI < 256)
            {
                // 重置该客户端的超时计时器
                Netplay.Clients[whoAmI].TimeOutTimer = 0;
            }
            else
            {
                // 如果是客户端，重置连接的超时计时器
                Netplay.Connection.TimeOutTimer = 0;
            }

            // 定义消息类型字节变量
            byte messageTypeByte = 0;
            // 定义读取偏移量变量
            int readOffset = 0;
            // 读取偏移量设为起始位置 + 1（跳过消息类型字节）
            readOffset = start + 1;
            // 从缓冲区读取消息类型，并赋值给输出参数 messageType
            messageTypeByte = (byte)(messageType = readBuffer[start]);
            // 如果消息类型超出有效范围
            if (messageTypeByte >= MessageID.Count)
            {
                // 直接返回，不处理无效消息
                return;
            }

            // 统计读取的消息数量和长度，用于网络诊断
            Main.ActiveNetDiagnosticsUI.CountReadMessage(messageTypeByte, length);
            // 如果是客户端且连接状态有最大值限制
            // if (Main.netMode == 1 && Netplay.Connection.StatusMax > 0)
            // {
            //     // 增加连接状态计数
            //     Netplay.Connection.StatusCount++;
            // }

            // 如果开启了详细的网络调试模式
            if (Main.verboseNetplay)
            {
                // 遍历消息内容的每个字节（仅作循环，可能用于调试断点或性能测试）
                for (int i = start; i < start + length; i++)
                {
                }

                // 再次遍历消息内容的每个字节
                for (int j = start; j < start + length; j++)
                {
                    // 读取字节但不做处理，仅用于确保数据可读或调试
                    _ = readBuffer[j];
                }
            }

            // 如果是服务器端，且消息类型不是密码验证(38)，且客户端状态为 -1（未验证）
            if (Main.netMode == 2 && messageTypeByte != 38 && Netplay.Clients[whoAmI].State == -1)
            {
                // 发送错误消息给客户端，提示需要密码
                NetMessage.TrySendData(2, whoAmI, -1, Lang.mp[1].ToNetworkText());
                // 返回，中断处理
                return;
            }

            // 如果是服务器端

            {
                // 如果客户端状态小于 10，且消息类型不是允许的几种初始消息类型
                if (Netplay.Clients[whoAmI].State < 10 && messageTypeByte > 12 && messageTypeByte != 93 && messageTypeByte != 16 && messageTypeByte != 42 && messageTypeByte != 50 && messageTypeByte != 38 && messageTypeByte != 68 && messageTypeByte != 147 && messageTypeByte != 161)
                {
                    // 踢出玩家，提示协议错误
                    NetMessage.BootPlayer(whoAmI, Lang.mp[2].ToNetworkText());
                }

                // 如果客户端状态为 0（刚连接），且消息类型不是连接请求(1)
                if (Netplay.Clients[whoAmI].State == 0 && messageTypeByte != 1)
                {
                    // 踢出玩家，提示协议错误
                    NetMessage.BootPlayer(whoAmI, Lang.mp[2].ToNetworkText());
                }
            }

            // 如果读取器为空
            if (reader == null)
            {
                // 重置读取器
                ResetReader();
            }

            // 设置读取流的位置到数据开始处
            reader.BaseStream.Position = readOffset;
            // 根据消息类型进行处理
            switch (messageTypeByte)
            {
                // 消息类型 1: 连接请求
                case 1:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        string clientVersion = reader.ReadString();

                        //阶段3.逻辑处理
                        // 如果是专用服务器且玩家被封禁
                        if (Main.dedServ && Netplay.IsBanned(Netplay.Clients[whoAmI].Socket.GetRemoteAddress()))
                        {
                            //阶段4.响应/同步
                            // 发送错误消息，提示玩家已被封禁
                            NetMessage.TrySendData(2, whoAmI, -1, Lang.mp[3].ToNetworkText());
                        }
                        else
                        {
                            // 如果客户端状态不是 0（初始连接状态）
                            if (Netplay.Clients[whoAmI].State != 0)
                            {
                                break;
                            }

                            // 验证版本号是否匹配 "Terraria" + 版本号 (318)
                            if (clientVersion == "Terraria" + 318)
                            {
                                // 如果服务器没有密码
                                if (string.IsNullOrEmpty(Netplay.ServerPassword))
                                {
                                    // 设置客户端状态为 1（已连接）
                                    Netplay.Clients[whoAmI].State = 1;
                                    //阶段4.响应/同步
                                    // 发送确认连接消息
                                    NetMessage.TrySendData(3, whoAmI);
                                }
                                else
                                {
                                    // 如果服务器有密码，设置状态为 -1（等待密码）
                                    Netplay.Clients[whoAmI].State = -1;
                                    //阶段4.响应/同步
                                    // 发送密码请求消息
                                    NetMessage.TrySendData(37, whoAmI);
                                }
                            }
                            else
                            {
                                //阶段4.响应/同步
                                // 如果版本不匹配，发送错误消息
                                NetMessage.TrySendData(2, whoAmI, -1, Lang.mp[4].ToNetworkText());
                            }
                        }
                    }
                    break;
                // 消息类型 2: 断开连接 / 状态文本
                case 2:
                    break;
                // 消息类型 3: 设置玩家ID（通常是服务器分配给客户端的ID）
                case 3:
                    // 如果是客户端
                    break;
                // 消息类型 4: 同步玩家外观和基本数据
                case 4:
                    {
                        if (UseComprehensiveAnalysis)
                        {
                            // 综合函数数据分析 - 启用新版数据包格式重排处理
                            var processor = new SyncPlayerProcessor();
                            processor.Process(new PacketContext
                            {
                                WhoAmI = whoAmI,
                                Reader = reader,
                                MessageId = 4
                            });
                            break;
                        }

                        //使用规定内存排序,这样就可以调换成员为使其更具协议规定的格式
                        //创建一个处理数据流管道,或者在代码中定义一个处理流,数据流管道拥有更大的自由度

                        //阶段1.依赖项读取
                        //例如某个targetPlayer.skinVariant需要BitsByte difficultyAndExtraAccessoryFlags的某一位存在才能进行读取
                        //那么设计为所有标记为位掩码和依赖项的在此阶段读取
                        // 读取难度和额外饰品标志位
                        BitsByte difficultyAndExtraAccessoryFlags = reader.ReadByte();
                        // 读取火把和特殊能力标志位
                        BitsByte torchAndAbilityFlags = reader.ReadByte();
                        // 读取消耗品使用状态标志位
                        BitsByte consumableFlags = reader.ReadByte();
                        // 读取目标玩家ID
                        int targetPlayerId = reader.ReadByte();

                        //阶段2.普通流读取
                        //这里是正常流读取
                        // 获取目标玩家对象
                        Player targetPlayer = Main.player[whoAmI];
                        // 读取皮肤变体
                        targetPlayer.skinVariant = reader.ReadByte();
                        // 读取声音变体
                        targetPlayer.voiceVariant = reader.ReadByte();
                        // 读取声音音调偏移
                        targetPlayer.voicePitchOffset = reader.ReadSingle();
                        // 读取发型
                        targetPlayer.hair = reader.ReadByte();
                        // 读取并清理玩家名字
                        targetPlayer.name = reader.ReadString().Trim().Trim();
                        // 读取发色染料
                        targetPlayer.hairDye = reader.ReadByte();
                        // 读取饰品可见性
                        ReadAccessoryVisibility(reader, targetPlayer.hideVisibleAccessory);
                        // 读取隐藏杂项
                        targetPlayer.hideMisc = reader.ReadByte();
                        // 读取头发颜色
                        targetPlayer.hairColor = reader.ReadRGB();
                        // 读取皮肤颜色
                        targetPlayer.skinColor = reader.ReadRGB();
                        // 读取眼睛颜色
                        targetPlayer.eyeColor = reader.ReadRGB();
                        // 读取衬衫颜色
                        targetPlayer.shirtColor = reader.ReadRGB();
                        // 读取内衣颜色
                        targetPlayer.underShirtColor = reader.ReadRGB();
                        // 读取裤子颜色
                        targetPlayer.pantsColor = reader.ReadRGB();
                        // 读取鞋子颜色
                        targetPlayer.shoeColor = reader.ReadRGB();

                        //阶段3,额外handler处理数据
                        // 如果是服务器端，强制将ID设为当前连接的客户端ID（防止客户端修改他人数据）
                        //targetPlayerId = whoAmI;
                        // 设置玩家的 whoAmI
                        targetPlayer.whoAmI = whoAmI;

                        // 限制皮肤变体在有效范围内
                        targetPlayer.skinVariant = (int)MathHelper.Clamp(targetPlayer.skinVariant, 0f, PlayerVariantID.Count - 1);
                        // 限制声音变体在有效范围内
                        targetPlayer.voiceVariant = Utils.Clamp(targetPlayer.voiceVariant, 1, 4);
                        // 限制音调偏移在 -1 到 1 之间
                        targetPlayer.voicePitchOffset = Utils.Clamp(targetPlayer.voicePitchOffset, -1f, 1f);
                        // 如果音调偏移是 NaN，重置为 0
                        if (float.IsNaN(targetPlayer.voicePitchOffset))
                        {
                            targetPlayer.voicePitchOffset = 0f;
                        }

                        // 如果发型超出范围，重置为 0
                        if (targetPlayer.hair >= 228)
                        {
                            targetPlayer.hair = 0;
                        }

                        // 设置是否有额外饰品栏
                        targetPlayer.extraAccessory = difficultyAndExtraAccessoryFlags[2];

                        // 设置是否使用生物群落火把
                        targetPlayer.UsingBiomeTorches = torchAndAbilityFlags[0];
                        // 设置火把神事件状态
                        targetPlayer.happyFunTorchTime = torchAndAbilityFlags[1];
                        // 设置是否解锁生物群落火把
                        targetPlayer.unlockedBiomeTorches = torchAndAbilityFlags[2];
                        // 设置是否解锁超级矿车
                        targetPlayer.unlockedSuperCart = torchAndAbilityFlags[3];
                        // 设置是否启用超级矿车
                        targetPlayer.enabledSuperCart = torchAndAbilityFlags[4];

                        // 埃吉斯水晶
                        targetPlayer.usedAegisCrystal = consumableFlags[0];
                        // 埃吉斯果
                        targetPlayer.usedAegisFruit = consumableFlags[1];
                        // 奥术水晶
                        targetPlayer.usedArcaneCrystal = consumableFlags[2];
                        // 银河珍珠
                        targetPlayer.usedGalaxyPearl = consumableFlags[3];
                        // 软糖蠕虫
                        targetPlayer.usedGummyWorm = consumableFlags[4];
                        // 仙馔密酒
                        targetPlayer.usedAmbrosia = consumableFlags[5];
                        // 工匠面包
                        targetPlayer.ateArtisanBread = consumableFlags[6];

                         // 重置难度
                        targetPlayer.difficulty = 0;
                        // 解析难度标志位
                        if (difficultyAndExtraAccessoryFlags[0])
                        {
                            targetPlayer.difficulty = 1;
                        }

                        if (difficultyAndExtraAccessoryFlags[1])
                        {
                            targetPlayer.difficulty = 2;
                        }

                        if (difficultyAndExtraAccessoryFlags[3])
                        {
                            targetPlayer.difficulty = 3;
                        }

                        // 限制难度最大值为 3
                        if (targetPlayer.difficulty > 3)
                        {
                            targetPlayer.difficulty = 3;
                        }

                        // 检查是否有重名玩家
                        bool isNameDuplicate = false;
                        if (Netplay.Clients[whoAmI].State < 10)
                        {
                            for (int otherPlayerIndex = 0; otherPlayerIndex < 255; otherPlayerIndex++)
                            {
                                if (otherPlayerIndex != targetPlayerId && targetPlayer.name == Main.player[otherPlayerIndex].name && Netplay.Clients[otherPlayerIndex].IsActive)
                                {
                                    isNameDuplicate = true;
                                }
                            }
                        }

                        //阶段四.4为请求回应包
                        // 如果名字重复
                        if (isNameDuplicate)
                        {
                            NetMessage.TrySendData(2, whoAmI, -1, NetworkText.FromKey(Lang.mp[5].Key, targetPlayer.name));
                        }
                        // 如果名字太长
                        else if (targetPlayer.name.Length > Player.nameLen)
                        {
                            NetMessage.TrySendData(2, whoAmI, -1, NetworkText.FromKey("Net.NameTooLong"));
                        }
                        // 如果名字为空
                        else if (targetPlayer.name == "")
                        {
                            NetMessage.TrySendData(2, whoAmI, -1, NetworkText.FromKey("Net.EmptyName"));
                        }
                        // 如果玩家是创造模式但世界不是
                        else if (targetPlayer.difficulty == 3 && !Main.IsJourneyMode)
                        {
                            NetMessage.TrySendData(2, whoAmI, -1, NetworkText.FromKey("Net.PlayerIsCreativeAndWorldIsNotCreative"));
                        }
                        // 如果玩家不是创造模式但世界是
                        else if (targetPlayer.difficulty != 3 && Main.IsJourneyMode)
                        {
                            NetMessage.TrySendData(2, whoAmI, -1, NetworkText.FromKey("Net.PlayerIsNotCreativeAndWorldIsCreative"));
                        }
                        else
                        {
                            // 更新服务器上的客户端名称
                            Netplay.Clients[whoAmI].Name = targetPlayer.name;
                            Netplay.Clients[whoAmI].Name = targetPlayer.name;
                            // 向其他客户端同步该玩家数据
                            NetMessage.TrySendData(4, -1, whoAmI, null, targetPlayerId);
                        }

                        break;
                    }

                // 消息类型 5: 同步物品栏/装备栏/银行等物品槽数据
                case 5:
                    {
                        //阶段1.依赖项读取
                        // 读取物品标志位
                        BitsByte itemFlags = reader.ReadByte();
                        // 读取玩家ID
                        int playerId = reader.ReadByte();
                        // 读取物品槽索引
                        int slotIndex = reader.ReadInt16();

                        //阶段2.普通流读取
                        // 读取堆叠数量
                        int stackSize = reader.ReadInt16();
                        // 读取前缀ID
                        int prefixId = reader.ReadByte();
                        // 读取物品类型
                        int itemType = reader.ReadInt16();

                        //阶段3,额外handler处理数据
                        // 是否收藏
                        bool isFavorited = itemFlags[0];
                        // 是否被阻塞（如虚空袋被禁用）
                        bool isBlocked = itemFlags[1];

                        // 如果是服务器端，强制将ID设为当前连接的客户端ID
                        if (Main.netMode == 2)
                        {
                            playerId = whoAmI;
                        }

                        // 如果是本地玩家且未开启服务端角色（SSC），且未锁定物品栏，则不处理
                        if (playerId == Main.myPlayer && !Main.ServerSideCharacter && !Main.player[playerId].HasLockedInventory())
                        {
                            break;
                        }

                        // 获取目标玩家对象
                        Player targetPlayer = Main.player[playerId];
                        // 锁定玩家对象以进行线程安全操作
                        lock (targetPlayer)
                        {
                            // 创建物品槽引用
                            PlayerItemSlotID.SlotReference slot = new PlayerItemSlotID.SlotReference(targetPlayer, slotIndex);
                            // 创建客户端玩家的物品槽引用
                            PlayerItemSlotID.SlotReference slotReference = new PlayerItemSlotID.SlotReference(Main.clientPlayer, slotIndex);
                            // 创建新物品对象
                            Item item = new Item();
                            // 设置物品默认属性
                            item.SetDefaults(itemType);
                            // 设置堆叠数量
                            item.stack = stackSize;
                            // 设置前缀
                            item.Prefix(prefixId);
                            // 设置收藏状态
                            item.favorited = isFavorited;
                            // 将物品放入槽位
                            slot.Item = item;
                            // 如果是本地玩家且非SSC模式
                            if (playerId == Main.myPlayer && !Main.ServerSideCharacter)
                            {
                                // 克隆物品到客户端玩家引用的槽位
                                slotReference.Item = item.Clone();
                            }

                            // 如果槽位属于虚空袋范围
                            if (slotIndex >= PlayerItemSlotID.Bank4_0 && slotIndex < PlayerItemSlotID.Loadout1_Armor_0)
                            {

                            }
                            // 如果是普通物品栏
                            else if (slotIndex <= 58)
                            {
                                // 如果是本地玩家且是鼠标物品槽（58）
                                if (playerId == Main.myPlayer && slotIndex == 58)
                                {
                                    // 更新鼠标物品
                                    Main.mouseItem = item.Clone();
                                }


                            }

                            //阶段四.4为请求回应包
                            // 获取可转发的槽位数组
                            bool[] canRelay = PlayerItemSlotID.CanRelay;
                            // 如果是服务器端，且是当前客户端发来的数据，且该槽位允许转发
                            if (Main.netMode == 2 && playerId == whoAmI && canRelay.IndexInRange(slotIndex) && canRelay[slotIndex])
                            {
                                // 转发给其他客户端
                                NetMessage.TrySendData(5, -1, whoAmI, null, playerId, slotIndex);
                            }

                            break;
                        }
                    }

                // 消息类型 6: 请求世界信息
                case 6:
                    // 如果是服务器端

                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取

                        //阶段3.逻辑处理
                        // 如果客户端状态为 1（已连接）
                        if (Netplay.Clients[whoAmI].State == 1)
                        {
                            // 更新状态为 2（接收玩家数据）
                            Netplay.Clients[whoAmI].State = 2;
                        }

                        //阶段4.响应/同步
                        // 发送世界信息给客户端
                        NetMessage.TrySendData(7, whoAmI);
                        // 同步入侵事件信息
                        Main.SyncAnInvasion(whoAmI);
                    }

                    break;
                // 消息类型 7: 世界数据
                case 7:

                    break;
                // 消息类型 8: 请求图格数据
                case 8:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        // 读取请求的X坐标
                        int requestedX = reader.ReadInt32();
                        // 读取请求的Y坐标
                        int requestedY = reader.ReadInt32();
                        // 读取请求的队伍ID
                        int requestedTeam = reader.ReadByte();

                        //阶段3.逻辑处理
                        // 标记请求是否有效
                        bool isValidRequest = true;
                        // 如果请求坐标为 -1，视为无效
                        if (requestedX == -1 || requestedY == -1)
                        {
                            isValidRequest = false;
                        }
                        // 如果请求X坐标超出范围（预留边界）
                        else if (requestedX < 10 || requestedX > Main.maxTilesX - 10)
                        {
                            isValidRequest = false;
                        }
                        // 如果请求Y坐标超出范围（预留边界）
                        else if (requestedY < 10 || requestedY > Main.maxTilesY - 10)
                        {
                            isValidRequest = false;
                        }

                        // 是否使用队伍重生点
                        bool useTeamSpawn = false;
                        // 如果开启了基于队伍的重生点且队伍ID不为0
                        if (Main.teamBasedSpawnsSeed && requestedTeam != 0)
                        {
                            useTeamSpawn = true;
                        }

                        // 计算需要同步的区块范围（以出生点为中心）
                        int sectionXMin = Netplay.GetSectionX(Main.spawnTileX) - 2;
                        int sectionYMin = Netplay.GetSectionY(Main.spawnTileY) - 1;
                        int sectionXMax = sectionXMin + 5;
                        int sectionYMax = sectionYMin + 3;
                        // 边界检查
                        if (sectionXMin < 0)
                        {
                            sectionXMin = 0;
                        }

                        if (sectionXMax >= Main.maxSectionsX)
                        {
                            sectionXMax = Main.maxSectionsX;
                        }

                        if (sectionYMin < 0)
                        {
                            sectionYMin = 0;
                        }

                        if (sectionYMax >= Main.maxSectionsY)
                        {
                            sectionYMax = Main.maxSectionsY;
                        }

                        // 计算总区块数
                        int totalSections = (sectionXMax - sectionXMin) * (sectionYMax - sectionYMin);
                        // 存储需要同步的区块坐标
                        List<Point> sectionsToSync = new List<Point>();
                        // 添加出生点附近的区块
                        for (int x = sectionXMin; x < sectionXMax; x++)
                        {
                            for (int y = sectionYMin; y < sectionYMax; y++)
                            {
                                sectionsToSync.Add(new Point(x, y));
                            }
                        }

                        // 请求区域的区块范围变量
                        int reqSectionXMin = -1;
                        int reqSectionYMin = -1;
                        // 如果请求有效，添加请求区域附近的区块
                        if (isValidRequest)
                        {
                            // 计算请求坐标所在的区块范围
                            requestedX = Netplay.GetSectionX(requestedX) - 2;
                            requestedY = Netplay.GetSectionY(requestedY) - 1;
                            reqSectionXMin = requestedX + 5;
                            reqSectionYMin = requestedY + 3;
                            // 边界检查
                            if (requestedX < 0)
                            {
                                requestedX = 0;
                            }

                            if (reqSectionXMin >= Main.maxSectionsX)
                            {
                                reqSectionXMin = Main.maxSectionsX - 1;
                            }

                            if (requestedY < 0)
                            {
                                requestedY = 0;
                            }

                            if (reqSectionYMin >= Main.maxSectionsY)
                            {
                                reqSectionYMin = Main.maxSectionsY - 1;
                            }

                            // 遍历请求区域的区块
                            for (int x = requestedX; x <= reqSectionXMin; x++)
                            {
                                for (int y = requestedY; y <= reqSectionYMin; y++)
                                {
                                    // 如果该区块不在出生点区域内，则添加
                                    if (x < sectionXMin || x >= sectionXMax || y < sectionYMin || y >= sectionYMax)
                                    {
                                        sectionsToSync.Add(new Point(x, y));
                                        totalSections++;
                                    }
                                }
                            }
                        }

                        // 队伍重生点的区块范围变量
                        int teamSectionXMin = -1;
                        int teamSectionYMin = -1;
                        int teamSectionXMax = -1;
                        int teamSectionYMax = -1;
                        // 如果使用队伍重生点
                        if (useTeamSpawn)
                        {
                            Point spawnPoint = Point.Zero;
                            // 尝试获取队伍额外重生点
                            if (ExtraSpawnPointManager.TryGetExtraSpawnPointForTeam(requestedTeam, out spawnPoint))
                            {
                                teamSectionXMin = spawnPoint.X;
                                teamSectionYMin = spawnPoint.Y;
                                // 计算区块范围
                                teamSectionXMin = Netplay.GetSectionX(teamSectionXMin) - 2;
                                teamSectionYMin = Netplay.GetSectionY(teamSectionYMin) - 1;
                                teamSectionXMax = teamSectionXMin + 5;
                                teamSectionYMax = teamSectionYMin + 3;
                                // 边界检查
                                if (teamSectionXMin < 0)
                                {
                                    teamSectionXMin = 0;
                                }

                                if (teamSectionXMax >= Main.maxSectionsX)
                                {
                                    teamSectionXMax = Main.maxSectionsX;
                                }

                                if (teamSectionYMin < 0)
                                {
                                    teamSectionYMin = 0;
                                }

                                if (teamSectionYMax >= Main.maxSectionsY)
                                {
                                    teamSectionYMax = Main.maxSectionsY;
                                }

                                // 遍历队伍重生点区域的区块
                                for (int x = teamSectionXMin; x <= teamSectionXMax; x++)
                                {
                                    for (int y = teamSectionYMin; y <= teamSectionYMax; y++)
                                    {
                                        // 检查是否已经在出生点区域或请求区域中
                                        bool alreadyAdded = false;
                                        if (x >= sectionXMin && x < sectionXMax && y >= sectionYMin && y < sectionYMax)
                                        {
                                            alreadyAdded = true;
                                        }

                                        if (!alreadyAdded && isValidRequest && x >= requestedX && x <= reqSectionXMin && y >= requestedY && y <= reqSectionYMin)
                                        {
                                            alreadyAdded = true;
                                        }

                                        // 如果未添加，则加入列表
                                        if (!alreadyAdded)
                                        {
                                            sectionsToSync.Add(new Point(x, y));
                                            totalSections++;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                useTeamSpawn = false;
                            }
                        }

                        // 同步传送门信息
                        PortalHelper.SyncPortalsOnPlayerJoin(whoAmI, 1, sectionsToSync, out var portalSections);
                        totalSections += portalSections.Count;
                        // 如果客户端状态为 2（接收玩家数据），更新为 3（请求世界数据）
                        if (Netplay.Clients[whoAmI].State == 2)
                        {
                            Netplay.Clients[whoAmI].State = 3;
                        }

                        //阶段4.响应/同步
                        // 向当前客户端发送世界信息
                        NetMessage.TrySendData(7, whoAmI);
                        // 发送区块流数据（消息类型 9）
                        NetMessage.TrySendData(9, whoAmI, -1, Lang.inter[44].ToNetworkText(), totalSections);
                        // 设置客户端的区块同步状态
                        Netplay.Clients[whoAmI].StatusText2 = Language.GetTextValue("Net.IsReceivingTileData");
                        Netplay.Clients[whoAmI].StatusMax += totalSections;
                        // 遍历所有需要同步的区块，发起传送
                        for (int x = sectionXMin; x < sectionXMax; x++)
                        {
                            for (int y = sectionYMin; y < sectionYMax; y++)
                            {
                                NetMessage.SendSection(whoAmI, x, y);
                            }
                        }

                        if (isValidRequest)
                        {
                            for (int x = requestedX; x <= reqSectionXMin; x++)
                            {
                                for (int y = requestedY; y <= reqSectionYMin; y++)
                                {
                                    NetMessage.SendSection(whoAmI, x, y);
                                }
                            }
                        }

                        if (useTeamSpawn)
                        {
                            for (int x = teamSectionXMin; x <= teamSectionXMax; x++)
                            {
                                for (int y = teamSectionYMin; y <= teamSectionYMax; y++)
                                {
                                    NetMessage.SendSection(whoAmI, x, y);
                                }
                            }
                        }

                        for (int i = 0; i < portalSections.Count; i++)
                        {
                            NetMessage.SendSection(whoAmI, portalSections[i].X, portalSections[i].Y);
                        }

                        // 同步掉落物信息
                        for (int itemIndex = 0; itemIndex < 400; itemIndex++)
                        {
                            if (Main.item[itemIndex].active)
                            {
                                NetMessage.TrySendData(21, whoAmI, -1, null, itemIndex);
                                NetMessage.TrySendData(22, whoAmI, -1, null, itemIndex);
                            }
                        }

                        // 同步NPC信息
                        for (int npcIndex = 0; npcIndex < Main.maxNPCs; npcIndex++)
                        {
                            if (Main.npc[npcIndex].active)
                            {
                                NetMessage.TrySendData(23, whoAmI, -1, null, npcIndex);
                                NetMessage.TrySendData(54, whoAmI, -1, null, npcIndex);
                            }
                        }

                        // 同步弹幕信息
                        for (int projIndex = 0; projIndex < 1000; projIndex++)
                        {
                            if (Main.projectile[projIndex].active && (Main.projPet[Main.projectile[projIndex].type] || Main.projectile[projIndex].netImportant))
                            {
                                NetMessage.TrySendData(27, whoAmI, -1, null, projIndex);
                            }
                        }

                        // 同步旗帜、Bestiary、创意模式等信息
                        NetManager.Instance.SendToClient(BannerSystem.NetBannersModule.WriteFullState(), whoAmI);
                        NetMessage.TrySendData(57, whoAmI);
                        NetMessage.TrySendData(103);
                        NetMessage.TrySendData(101, whoAmI);
                        NetMessage.TrySendData(136, whoAmI);
                        Main.BestiaryTracker.OnPlayerJoining(whoAmI);
                        CreativePowerManager.Instance.SyncThingsToJoiningPlayer(whoAmI);
                        Main.PylonSystem.OnPlayerJoining(whoAmI);
                        NetMessage.TrySendData(49, whoAmI);
                        break;
                    }

                // 消息类型 9: 状态文本
                case 9:
                    // if (Main.netMode == 1)
                    // {
                    //     Netplay.Connection.StatusMax += reader.ReadInt32();
                    //     Netplay.Connection.StatusText = NetworkText.Deserialize(reader).ToString();
                    //     BitsByte connectionFlags = reader.ReadByte();
                    //     BitsByte serverSpecialFlags = Netplay.Connection.ServerSpecialFlags;
                    //     serverSpecialFlags[0] = connectionFlags[0];
                    //     serverSpecialFlags[1] = connectionFlags[1];
                    //     Netplay.Connection.ServerSpecialFlags = serverSpecialFlags;
                    // }

                    break;
                // 消息类型 10: 图格数据（压缩）
                case 10:
                    // if (Main.netMode == 1)
                    // {
                    //     NetMessage.DecompressTileBlock(reader.BaseStream);
                    // }

                    break;
                // 消息类型 11: 图格帧更新
                case 11:
                    // if (Main.netMode == 1)
                    // {
                    //     WorldGen.SectionTileFrame(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
                    // }

                    break;
                // 消息类型 12: 玩家重生
                case 12:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        // 读取玩家ID
                        int playerId = reader.ReadByte();
                        // 如果是服务器端，强制设为当前客户端ID

                        {
                            playerId = whoAmI;
                        }

                        // 获取玩家实例
                        Player player = Main.player[playerId];
                        // 读取重生坐标和状态
                        player.SpawnX = reader.ReadInt16();
                        player.SpawnY = reader.ReadInt16();
                        player.respawnTimer = reader.ReadInt32();
                        player.numberOfDeathsPVE = reader.ReadInt16();
                        player.numberOfDeathsPVP = reader.ReadInt16();
                        player.team = reader.ReadByte();

                        //阶段3.逻辑处理
                        // 如果正在重生倒计时，标记为死亡
                        if (player.respawnTimer > 0)
                        {
                            player.dead = true;
                        }

                        // 读取重生上下文并执行重生
                        PlayerSpawnContext playerSpawnContext = (PlayerSpawnContext)reader.ReadByte();
                        player.Spawn(playerSpawnContext);
                        // 如果不是服务器端或客户端状态不足，跳过
                        if (Main.netMode != 2 || Netplay.Clients[whoAmI].State < 3)
                        {
                            break;
                        }

                        // 如果客户端状态为 3，更新为 10（游戏进行中）
                        if (Netplay.Clients[whoAmI].State == 3)
                        {
                            Netplay.Clients[whoAmI].State = 10;
                            NetMessage.buffer[whoAmI].broadcast = true;
                            //阶段4.响应/同步
                            NetMessage.SyncConnectedPlayer(whoAmI);
                            // 检查是否算作主机
                            bool countsAsHost = NetMessage.DoesPlayerSlotCountAsAHost(whoAmI);
                            Main.countsAsHostForGameplay[whoAmI] = countsAsHost;
                            if (NetMessage.DoesPlayerSlotCountAsAHost(whoAmI))
                            {
                                NetMessage.TrySendData(139, whoAmI, -1, null, whoAmI, countsAsHost.ToInt());
                            }

                            // 同步玩家数据给其他客户端
                            NetMessage.TrySendData(12, -1, whoAmI, null, whoAmI, (int)(byte)playerSpawnContext);
                            NetMessage.TrySendData(129, whoAmI);
                            NetMessage.greetPlayer(whoAmI);
                            // 注册生物图鉴击杀
                            if (Main.player[playerId].unlockedBiomeTorches)
                            {
                                NPC nPC = new NPC();
                                nPC.SetDefaults(664);
                                Main.BestiaryTracker.Kills.RegisterKill(nPC);
                            }
                        }
                        else
                        {
                            // 发送重生信息给其他客户端
                            NetMessage.TrySendData(12, -1, whoAmI, null, whoAmI, (int)(byte)playerSpawnContext);
                        }

                        break;
                    }

                // 消息类型 13: 更新玩家状态
                case 13:
                    {
                        //阶段1.依赖项读取
                        // 读取控制和状态标志位
                        BitsByte controlFlags1 = reader.ReadByte();
                        BitsByte controlFlags2 = reader.ReadByte();
                        BitsByte controlFlags3 = reader.ReadByte();
                        BitsByte controlFlags4 = reader.ReadByte();
                        // 读取玩家ID
                        int playerId = reader.ReadByte();

                        //阶段2.普通流读取
                        // 读取选中物品状态
                        byte selectedItem = reader.ReadByte();
                        // 读取位置和速度
                        Vector2 position = reader.ReadVector2();
                        Vector2 velocity = Vector2.Zero;
                        if (controlFlags2[2])
                        {
                            velocity = reader.ReadVector2();
                        }

                        // 读取坐骑
                        ushort mountId = 0;
                        if (controlFlags2[7])
                        {
                            mountId = reader.ReadUInt16();
                        }

                        // 读取回城药水位置
                        Vector2? potionOfReturnOriginalUsePosition = null;
                        Vector2? potionOfReturnHomePosition = null;
                        if (controlFlags3[6])
                        {
                            potionOfReturnOriginalUsePosition = reader.ReadVector2();
                            potionOfReturnHomePosition = reader.ReadVector2();
                        }

                        // 读取摄像机目标
                        Vector2? netCameraTarget = null;
                        if (controlFlags4[5])
                        {
                            netCameraTarget = reader.ReadVector2();
                        }

                        //阶段3.逻辑处理
                        // 如果是服务器端，强制将ID设为当前连接的客户端ID
                        if (Main.netMode == 2)
                        {
                            playerId = whoAmI;
                        }

                        // 获取玩家实例
                        Player player = Main.player[playerId];
                        // 应用控制状态
                        player.controlUp = controlFlags1[0];
                        player.controlDown = controlFlags1[1];
                        player.controlLeft = controlFlags1[2];
                        player.controlRight = controlFlags1[3];
                        player.controlJump = controlFlags1[4];
                        player.controlUseItem = controlFlags1[5];
                        player.direction = (controlFlags1[6] ? 1 : (-1));
                        // 应用滑轮状态
                        if (controlFlags2[0])
                        {
                            player.pulley = true;
                            player.pulleyDir = (byte)((!controlFlags2[1]) ? 1u : 2u);
                        }
                        else
                        {
                            player.pulley = false;
                        }

                        // 应用隐身和重力状态
                        player.vortexStealthActive = controlFlags2[3];
                        player.gravDir = (controlFlags2[4] ? 1 : (-1));
                        player.TryTogglingShield(controlFlags2[5]);
                        player.ghost = controlFlags2[6];
                        // 阶段3.逻辑处理
                        // 应用选中物品
                        player.selectedItemState.Select(selectedItem);

                        // 如果有未确认的传送，保持原位置
                        if (player.unacknowledgedTeleports > 0)
                        {
                            position = player.position;
                            velocity = player.velocity;
                        }

                        // 更新位置和速度
                        player.position = position;
                        player.velocity = velocity;
                        Vector2 originalPosition = player.position;

                        // 处理坐骑
                        if (controlFlags2[7])
                        {
                            player.mount.SetMount(mountId, player);
                        }
                        else
                        {
                            player.mount.Dismount(player);
                        }

                        // 处理回城药水位置
                        if (controlFlags3[6])
                        {
                            player.PotionOfReturnOriginalUsePosition = potionOfReturnOriginalUsePosition;
                            player.PotionOfReturnHomePosition = potionOfReturnHomePosition;
                        }
                        else
                        {
                            player.PotionOfReturnOriginalUsePosition = null;
                            player.PotionOfReturnHomePosition = null;
                        }

                        // 应用其他状态标志
                        player.tryKeepingHoveringUp = controlFlags3[0];
                        player.IsVoidVaultEnabled = controlFlags3[1];
                        player.sitting.isSitting = controlFlags3[2];
                        player.downedDD2EventAnyDifficulty = controlFlags3[3];
                        player.petting.isPetting = controlFlags3[4];
                        player.petting.isPetSmall = controlFlags3[5];
                        player.tryKeepingHoveringDown = controlFlags3[7];
                        player.sleeping.SetIsSleepingAndAdjustPlayerRotation(player, controlFlags4[0]);
                        player.autoReuseAllWeapons = controlFlags4[1];
                        player.controlDownHold = controlFlags4[2];
                        player.isOperatingAnotherEntity = controlFlags4[3];
                        player.controlUseTile = controlFlags4[4];
                        player.netCameraTarget = netCameraTarget;
                        player.lastItemUseAttemptSuccess = controlFlags4[6];

                        // 交换位置以进行必要的更新
                        Utils.Swap(ref originalPosition, ref player.position);
                        // 如果是服务器端且玩家已完全连接，转发数据
                        if (Main.netMode == 2 && Netplay.Clients[whoAmI].State == 10)
                        {
                            NetMessage.TrySendData(13, -1, whoAmI, null, playerId);
                        }

                        // 恢复位置
                        Utils.Swap(ref originalPosition, ref player.position);
                        break;
                    }

                // 消息类型 14: 玩家激活状态
                case 14:
                    {


                        break;
                    }

                // 消息类型 16: 玩家生命值
                case 16:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        // 读取玩家ID
                        int playerId = reader.ReadByte();
                        // 如果不是自己或开启了服务端角色
                        if (playerId != Main.myPlayer || Main.ServerSideCharacter)
                        {
                            // 服务器端强制设为当前客户端ID

                            {
                                playerId = whoAmI;
                            }

                            // 获取玩家实例
                            Player player = Main.player[playerId];
                            // 读取生命值
                            player.statLife = reader.ReadInt16();
                            player.statLifeMax = reader.ReadInt16();
                            //阶段3.逻辑处理
                        // 修正最大生命值下限
                        if (player.statLifeMax < 20)
                        {
                            player.statLifeMax = 20;
                        }

                        // 判断是否死亡
                        player.dead = player.statLife <= 0;
                        //阶段4.响应/同步
                        // 如果是服务器端，转发数据

                        {
                            NetMessage.TrySendData(16, -1, whoAmI, null, playerId);
                        }
                        }

                        break;
                    }

                // 消息类型 17: 图格修改
                case 17:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        // 读取操作类型
                        byte action = reader.ReadByte();
                        // 读取坐标
                        int tileX = reader.ReadInt16();
                        int tileY = reader.ReadInt16();
                        // 读取类型或样式
                        short typeOrStyle = reader.ReadInt16();
                        // 读取前缀或额外数据
                        int prefix = reader.ReadByte();

                        //阶段3.逻辑处理
                        // 标记是否操作失败（如挖掘失败）
                        bool fail = typeOrStyle == 1;

                        // 检查坐标是否在世界范围内
                        if (!WorldGen.InWorld(tileX, tileY, 3))
                        {
                            break;
                        }

                        // 确保图格对象存在
                        if (Main.tile[tileX, tileY] == null)
                        {
                            Main.tile[tileX, tileY] = new Tile();
                        }

                        // 服务器端防作弊检查

                        {
                            if (!fail)
                            {
                                if (action == 0 || action == 2 || action == 4)
                                {
                                    Netplay.Clients[whoAmI].SpamDeleteBlock += 1f;
                                }

                                if (action == 1 || action == 3)
                                {
                                    Netplay.Clients[whoAmI].SpamAddBlock += 1f;
                                }
                            }

                            // 如果该区域未加载，强制标记为失败
                            if (!Netplay.Clients[whoAmI].TileSections[Netplay.GetSectionX(tileX), Netplay.GetSectionY(tileY)])
                            {
                                fail = true;
                            }
                        }

                        // 添加到地图更新队列
                        MapUpdateQueue.Add(tileX, tileY);

                        // 0: 破坏图格
                        if (action == 0)
                        {
                            WorldGen.KillTile(tileX, tileY, fail);
                            // 客户端清除打击效果
                            // if (Main.netMode == 1 && !fail)
                            // {
                            //     HitTile.ClearAllTilesAtThisLocation(tileX, tileY);
                            // }
                        }

                        // 1: 放置图格
                        bool isTileBreaker = false;
                        if (action == 1)
                        {
                            bool forced = true;
                            // 检查是否应该保留图格（如破裂的图格）
                            if (WorldGen.CheckTileBreakability2_ShouldTileSurvive(tileX, tileY))
                            {
                                isTileBreaker = true;
                                forced = false;
                            }

                            WorldGen.PlaceTile(tileX, tileY, typeOrStyle, mute: false, forced, -1, prefix);
                        }

                        // 2: 破坏墙壁
                        if (action == 2)
                        {
                            WorldGen.KillWall(tileX, tileY, fail);
                        }

                        // 3: 放置墙壁
                        if (action == 3)
                        {
                            WorldGen.PlaceWall(tileX, tileY, typeOrStyle);
                        }

                        // 4: 破坏图格（不掉落物品）
                        if (action == 4)
                        {
                            WorldGen.KillTile(tileX, tileY, fail, effectOnly: false, noItem: true);
                        }

                        // 5: 放置红电线
                        if (action == 5)
                        {
                            WorldGen.PlaceWire(tileX, tileY);
                        }

                        // 6: 破坏红电线
                        if (action == 6)
                        {
                            WorldGen.KillWire(tileX, tileY);
                        }

                        // 7: 锤击图格
                        if (action == 7)
                        {
                            WorldGen.PoundTile(tileX, tileY);
                        }

                        // 8: 放置致动器
                        if (action == 8)
                        {
                            WorldGen.PlaceActuator(tileX, tileY);
                        }

                        // 9: 破坏致动器
                        if (action == 9)
                        {
                            WorldGen.KillActuator(tileX, tileY);
                        }

                        // 10: 放置蓝电线
                        if (action == 10)
                        {
                            WorldGen.PlaceWire2(tileX, tileY);
                        }

                        // 11: 破坏蓝电线
                        if (action == 11)
                        {
                            WorldGen.KillWire2(tileX, tileY);
                        }

                        // 12: 放置绿电线
                        if (action == 12)
                        {
                            WorldGen.PlaceWire3(tileX, tileY);
                        }

                        // 13: 破坏绿电线
                        if (action == 13)
                        {
                            WorldGen.KillWire3(tileX, tileY);
                        }

                        // 14: 斜坡化图格
                        if (action == 14)
                        {
                            WorldGen.SlopeTile(tileX, tileY, typeOrStyle);
                        }

                        // 15: 调整矿车轨道
                        if (action == 15)
                        {
                            Minecart.FrameTrack(tileX, tileY, pound: true);
                        }

                        // 16: 放置黄电线
                        if (action == 16)
                        {
                            WorldGen.PlaceWire4(tileX, tileY);
                        }

                        // 17: 破坏黄电线
                        if (action == 17)
                        {
                            WorldGen.KillWire4(tileX, tileY);
                        }

                        // 处理逻辑门和其他特殊操作
                        switch (action)
                        {
                            case 18:
                                Wiring.SetCurrentUser(whoAmI);
                                Wiring.PokeLogicGate(tileX, tileY);
                                Wiring.SetCurrentUser();
                                return;
                            case 19:
                                Wiring.SetCurrentUser(whoAmI);
                                Wiring.Actuate(tileX, tileY);
                                Wiring.SetCurrentUser();
                                return;
                            case 20:
                                if (WorldGen.InWorld(tileX, tileY, 2))
                                {
                                    int type15 = Main.tile[tileX, tileY].type;
                                    WorldGen.KillTile(tileX, tileY, fail);
                                    typeOrStyle = (short)((Main.tile[tileX, tileY].active() && Main.tile[tileX, tileY].type == type15) ? 1 : 0);

                                    {
                                        NetMessage.TrySendData(17, -1, -1, null, action, tileX, tileY, typeOrStyle, prefix);
                                    }
                                }

                                return;
                            case 21:
                                WorldGen.ReplaceTile(tileX, tileY, (ushort)typeOrStyle, prefix);
                                break;
                        }

                        // 22: 替换墙壁
                        if (action == 22)
                        {
                            WorldGen.ReplaceWall(tileX, tileY, (ushort)typeOrStyle);
                        }

                        // 23: 锤击坡度
                        if (action == 23 && WorldGen.CanPoundTile(tileX, tileY))
                        {
                            Main.tile[tileX, tileY].slope((byte)typeOrStyle);
                            WorldGen.PoundTile(tileX, tileY);
                        }

                        //阶段4.响应/同步
                        // 服务器端同步

                        {
                            if (isTileBreaker)
                            {
                                NetMessage.SendTileSquare(-1, tileX, tileY, 5);
                            }
                            else if ((action != 1 && action != 21) || !TileID.Sets.Falling[typeOrStyle] || Main.tile[tileX, tileY].active())
                            {
                                NetMessage.TrySendData(17, -1, whoAmI, null, action, tileX, tileY, typeOrStyle, prefix);
                            }
                        }

                        break;
                    }

                // 消息类型 18: 游戏时间
                case 18:
                    //阶段1.依赖项读取

                    //阶段2.普通流读取
                    // if (Main.netMode == 1)
                    // {
                    //     Main.dayTime = reader.ReadByte() == 1;
                    //     Main.time = reader.ReadInt32();
                    //     Main.sunModY = reader.ReadInt16();
                    //     Main.moonModY = reader.ReadInt16();
                    // }

                    //阶段3.逻辑处理

                    //阶段4.响应/同步

                    break;
                // 消息类型 19: 门/陷阱门操作
                case 19:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        // 读取操作类型
                        byte action = reader.ReadByte();
                        // 读取坐标
                        int tileX = reader.ReadInt16();
                        int tileY = reader.ReadInt16();

                        //阶段3.逻辑处理
                        if (WorldGen.InWorld(tileX, tileY, 3))
                        {
                            // 读取方向
                            int direction = ((reader.ReadByte() != 0) ? 1 : (-1));
                            switch (action)
                            {
                                case 0:
                                    WorldGen.OpenDoor(tileX, tileY, direction);
                                    break;
                                case 1:
                                    WorldGen.CloseDoor(tileX, tileY, forced: true);
                                    break;
                                case 2:
                                    WorldGen.ShiftTrapdoor(tileX, tileY, direction == 1, 1);
                                    break;
                                case 3:
                                    WorldGen.ShiftTrapdoor(tileX, tileY, direction == 1, 0);
                                    break;
                                case 4:
                                    WorldGen.ShiftTallGate(tileX, tileY, closing: false, forced: true);
                                    break;
                                case 5:
                                    WorldGen.ShiftTallGate(tileX, tileY, closing: true, forced: true);
                                    break;
                            }

                            //阶段4.响应/同步

                            {
                                NetMessage.TrySendData(19, -1, whoAmI, null, action, tileX, tileY, (direction == 1) ? 1 : 0);
                            }
                        }

                        break;
                    }

                // 消息类型 20: 发送图格矩形区域（图格数据）
                case 20:
                    {
                        //阶段1.依赖项读取

                        //阶段2.普通流读取
                        // 读取起始坐标和宽高
                        int startX = reader.ReadInt16();
                        int startY = reader.ReadInt16();
                        ushort width = reader.ReadByte();
                        ushort height = reader.ReadByte();
                        byte changeTypeByte = reader.ReadByte();

                        //阶段3.逻辑处理
                        if (!WorldGen.InWorld(startX, startY, 3))//这种判断塞进处理管道里
                        {
                            break;
                        }

                        // 解析变更类型
                        TileChangeType changeType = TileChangeType.None;
                        if (Enum.IsDefined(typeof(TileChangeType), changeTypeByte))
                        {
                            changeType = (TileChangeType)changeTypeByte;
                        }

                        // 触发图格变更事件
                        if (MessageBuffer.OnTileChangeReceived != null)
                        {
                            MessageBuffer.OnTileChangeReceived(startX, startY, Math.Max(width, height), changeType);
                        }

                        BitsByte flags1 = (byte)0;
                        BitsByte flags2 = (byte)0;
                        BitsByte flags3 = (byte)0;
                        Tile tile = null;
                        // 遍历区域内的图格
                        for (int x = startX; x < startX + width; x++)
                        {
                            for (int y = startY; y < startY + height; y++)
                            {
                                if (Main.tile[x, y] == null)
                                {
                                    Main.tile[x, y] = new Tile();
                                }

                                tile = Main.tile[x, y];
                                bool wasActive = tile.active();
                                // 读取标志位
                                flags1 = reader.ReadByte();
                                flags2 = reader.ReadByte();
                                flags3 = reader.ReadByte();
                                // 应用标志位
                                tile.active(flags1[0]);
                                tile.wall = (byte)(flags1[2] ? 1u : 0u);
                                bool hasLiquid = flags1[3];
                                // 如果是客户端，应用液体状态
                                // if (Main.netMode != 2)
                                // {
                                //     tile.liquid = (byte)(hasLiquid ? 1u : 0u);
                                // }

                                tile.wire(flags1[4]);
                                tile.halfBrick(flags1[5]);
                                tile.actuator(flags1[6]);
                                tile.inActive(flags1[7]);
                                tile.wire2(flags2[0]);
                                tile.wire3(flags2[1]);
                                // 读取颜色
                                if (flags2[2])
                                {
                                    tile.color(reader.ReadByte());
                                }

                                // 读取墙壁颜色
                                if (flags2[3])
                                {
                                    tile.wallColor(reader.ReadByte());
                                }

                                if (tile.active())
                                {
                                    int oldType = tile.type;
                                    tile.type = reader.ReadUInt16();
                                    // 如果图格帧重要，读取帧坐标
                                    if (Main.tileFrameImportant[tile.type])
                                    {
                                        tile.frameX = reader.ReadInt16();
                                        tile.frameY = reader.ReadInt16();
                                    }
                                    else if (!wasActive || tile.type != oldType)
                                    {
                                        tile.frameX = -1;
                                        tile.frameY = -1;
                                    }

                                    // 计算坡度
                                    byte slope = 0;
                                    if (flags2[4])
                                    {
                                        slope++;
                                    }

                                    if (flags2[5])
                                    {
                                        slope += 2;
                                    }

                                    if (flags2[6])
                                    {
                                        slope += 4;
                                    }

                                    tile.slope(slope);
                                }

                                tile.wire4(flags2[7]);
                                tile.fullbrightBlock(flags3[0]);
                                tile.fullbrightWall(flags3[1]);
                                tile.invisibleBlock(flags3[2]);
                                tile.invisibleWall(flags3[3]);
                                // 读取墙壁类型
                                if (tile.wall > 0)
                                {
                                    tile.wall = reader.ReadUInt16();
                                }

                                // 读取液体数据
                                if (hasLiquid)
                                {
                                    tile.liquid = reader.ReadByte();
                                    tile.liquidType(reader.ReadByte());
                                }
                            }
                        }

                        // 更新图格帧
                        WorldGen.RangeFrame(startX, startY, startX + width, startY + height);

                        //阶段4.响应/同步

                        {
                            NetMessage.TrySendData(messageTypeByte, -1, whoAmI, null, startX, startY, (int)width, (int)height, changeTypeByte);
                        }

                        break;
                    }

                case 21:
                case 90:
                case 145:
                case 148:
                    {
                        // 读取物品索引
                        int itemIndex = reader.ReadInt16();
                        // 读取位置
                        Vector2 position = reader.ReadVector2();
                        // 读取速度
                        Vector2 velocity = reader.ReadVector2();
                        // 读取堆叠数量
                        int stackSize = reader.ReadInt16();
                        // 读取前缀
                        int prefix = reader.ReadByte();
                        // 读取标志位
                        BitsByte itemFlags = reader.ReadByte();
                        // 是否激活
                        bool isActive = itemFlags[0];
                        // 是否有拥有者延迟
                        bool hasOwnerDelay = itemFlags[1];
                        // 读取物品类型
                        int itemType = reader.ReadInt16();
                        bool shimmered = false;
                        float shimmerTime = 0f;
                        int timeLeftInWhichTheItemCannotBeTakenByEnemies = 0;
                        if (messageTypeByte == 145)
                        {
                            // 读取微光状态
                            shimmered = reader.ReadBoolean();
                            shimmerTime = reader.ReadSingle();
                        }

                        if (messageTypeByte == 148)
                        {
                            // 读取敌人无法拾取时间
                            timeLeftInWhichTheItemCannotBeTakenByEnemies = reader.ReadByte();
                        }

                        WorldItem item = Main.item[itemIndex];

                        {
                            // 服务器逻辑
                            if (Main.timeItemSlotCannotBeReusedFor[itemIndex] > 0)
                            {
                                break;
                            }

                            // 如果索引为400，表示这是一个新生成的物品
                            bool isNewItem = itemIndex == 400;
                            if (isNewItem)
                            {
                                Item tempItem = new Item();
                                tempItem.SetDefaults(itemType);
                                // 生成新物品
                                itemIndex = Item.NewItem(new EntitySource_Sync(), (int)position.X, (int)position.Y, tempItem.width, tempItem.height, tempItem.type, stackSize, noBroadcast: true);
                                item = Main.item[itemIndex];
                                // 设置标志位
                                hasOwnerDelay = (itemFlags[1] = !isActive);
                            }
                            else
                            {
                                // 更新现有物品
                                int timeSinceTheItemHasBeenReservedForSomeone = item.timeSinceTheItemHasBeenReservedForSomeone;
                                if (item.playerIndexTheItemIsReservedFor != whoAmI)
                                {
                                    timeSinceTheItemHasBeenReservedForSomeone = 0;
                                }

                                item.playerIndexTheItemIsReservedFor = 255;
                                item.SetDefaults(itemType);
                                item.playerIndexTheItemIsReservedFor = whoAmI;
                                item.timeSinceTheItemHasBeenReservedForSomeone = timeSinceTheItemHasBeenReservedForSomeone;
                            }

                            item.Prefix(prefix);
                            item.stack = stackSize;
                            item.position = position;
                            item.velocity = velocity;
                            item.timeLeftInWhichTheItemCannotBeTakenByEnemies = timeLeftInWhichTheItemCannotBeTakenByEnemies;
                            if (messageTypeByte == 145)
                            {
                                item.shimmered = shimmered;
                                item.shimmerTime = shimmerTime;
                            }

                            if (hasOwnerDelay)
                            {
                                item.ownIgnore = whoAmI;
                                item.ownTime = 100;
                            }

                            if (isNewItem)
                            {
                                // 如果是新物品，广播给所有人
                                NetMessage.TrySendData(messageTypeByte, -1, -1, null, itemIndex, (int)(byte)itemFlags);
                                Main.item[itemIndex].FindOwner();
                            }
                            else
                            {
                                // 否则只发回给发送者（确认？）或者转发？这里是 SendData(..., -1, whoAmI, ...) -> 发给除 whoAmI 以外的所有人
                                NetMessage.TrySendData(messageTypeByte, -1, whoAmI, null, itemIndex);
                            }
                        }

                        break;
                    }

                case 151:
                    {
                        // 读取物品索引
                        int itemIndex = reader.ReadInt16();
                        WorldItem item = Main.item[itemIndex];
                        if ((Main.netMode != 2 || Main.timeItemSlotCannotBeReusedFor[itemIndex] <= 0) && (Main.netMode != 2 || item.playerIndexTheItemIsReservedFor == whoAmI))
                        {
                            item.playerIndexTheItemIsReservedFor = 255;
                            item.TurnToAir();

                            {
                                NetMessage.TrySendData(151, -1, whoAmI, null, itemIndex);
                            }
                        }

                        break;
                    }

                case 22:
                    {
                        // 读取物品索引
                        int itemIndex = reader.ReadInt16();
                        // 读取拥有者索引
                        int ownerIndex = reader.ReadByte();
                        // 读取位置
                        Vector2 position = reader.ReadVector2();
                        WorldItem item = Main.item[itemIndex];


                        break;
                    }

                case 23:
                    {

                        break;
                    }

                case 24:
                    {
                        // 读取NPC索引
                        int npcIndex = reader.ReadInt16();
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        Player player = Main.player[playerIndex];
                        // 玩家打击NPC
                        Main.npc[npcIndex].StrikeNPC(player.inventory[player.selectedItem].damage, player.inventory[player.selectedItem].knockBack, player.direction);

                        {
                            NetMessage.TrySendData(24, -1, whoAmI, null, npcIndex, playerIndex);
                            NetMessage.TrySendData(23, -1, -1, null, npcIndex);
                        }

                        break;
                    }

                case 27:
                    {

                        float[] array2 = ReUseTemporaryProjectileAI();
                        BitsByte bitsByte22 = reader.ReadByte();

                        int num226 = reader.ReadInt16();
                        Vector2 position5 = reader.ReadVector2();
                        Vector2 velocity7 = reader.ReadVector2();
                        int num227 = reader.ReadByte();
                        int num228 = reader.ReadInt16();

                        BitsByte bitsByte23 = (byte)(bitsByte22[2] ? reader.ReadByte() : 0);

                        array2[0] = (bitsByte22[0] ? reader.ReadSingle() : 0f);
                        array2[1] = (bitsByte22[1] ? reader.ReadSingle() : 0f);
                        int bannerIdToRespondTo = (bitsByte22[3] ? reader.ReadUInt16() : 0);
                        int damage3 = (bitsByte22[4] ? reader.ReadInt16() : 0);
                        float knockBack2 = (bitsByte22[5] ? reader.ReadSingle() : 0f);
                        int originalDamage = (bitsByte22[6] ? reader.ReadInt16() : 0);
                        int num229 = (bitsByte22[7] ? reader.ReadInt16() : (-1));

                        array2[2] = (bitsByte23[0] ? reader.ReadSingle() : 0f);













                        if (num229 >= 1000)
                        {
                            num229 = -1;
                        }
                        {
                            if (num228 == 949)
                            {
                                num227 = 255;
                            }
                            else
                            {
                                num227 = whoAmI;
                                if (Main.projHostile[num228])
                                {
                                    break;
                                }
                            }
                        }

                        int num230 = 1000;
                        for (int num231 = 0; num231 < 1000; num231++)
                        {
                            if (Main.projectile[num231].owner == num227 && Main.projectile[num231].identity == num226 && Main.projectile[num231].active)
                            {
                                num230 = num231;
                                break;
                            }
                        }

                        if (num230 == 1000)
                        {
                            for (int num232 = 0; num232 < 1000; num232++)
                            {
                                if (!Main.projectile[num232].active)
                                {
                                    num230 = num232;
                                    break;
                                }
                            }
                        }

                        if (num230 == 1000)
                        {
                            num230 = Projectile.FindOldestProjectile();
                        }

                        Projectile projectile = Main.projectile[num230];
                        if (!projectile.active || projectile.type != num228)
                        {
                            projectile.SetDefaults(num228);

                            {
                                Netplay.Clients[whoAmI].SpamProjectile += 1f;
                            }
                        }

                        projectile.identity = num226;
                        projectile.position = position5;
                        projectile.velocity = velocity7;
                        projectile.type = num228;
                        projectile.damage = damage3;
                        projectile.bannerIdToRespondTo = bannerIdToRespondTo;
                        projectile.originalDamage = originalDamage;
                        projectile.knockBack = knockBack2;
                        projectile.owner = num227;
                        for (int num233 = 0; num233 < Projectile.maxAI; num233++)
                        {
                            projectile.ai[num233] = array2[num233];
                        }

                        if (num229 >= 0)
                        {
                            projectile.projUUID = num229;
                            Main.projectileIdentity[num227, num229] = num230;
                        }

                        projectile.ProjectileFixDesperation();

                        {
                            NetMessage.TrySendData(27, -1, whoAmI, null, num230);
                        }

                        break;
                    }

                case 28:
                    {
                        int num211 = reader.ReadInt16();
                        int num212 = reader.ReadInt16();
                        float num213 = reader.ReadSingle();
                        int num214 = reader.ReadByte() - 1;
                        byte b14 = reader.ReadByte();

                        {
                            if (num212 < 0)
                            {
                                num212 = 0;
                            }

                            Main.npc[num211].PlayerInteraction(whoAmI);
                        }

                        if (num212 >= 0)
                        {
                            Main.npc[num211].StrikeNPC(num212, num213, num214, b14 == 1, noEffect: false, fromNet: true, (Main.netMode == 2) ? whoAmI : 255);
                        }
                        else
                        {
                            Main.npc[num211].life = 0;
                            Main.npc[num211].HitEffect();
                            Main.npc[num211].active = false;
                        }
                        {
                            NetMessage.TrySendData(28, -1, whoAmI, null, num211, num212, num213, num214, b14);
                            if (Main.npc[num211].life <= 0)
                            {
                                NetMessage.TrySendData(23, -1, -1, null, num211);
                            }

                            if (Main.npc[num211].realLife >= 0 && Main.npc[Main.npc[num211].realLife].life <= 0)
                            {
                                NetMessage.TrySendData(23, -1, -1, null, Main.npc[num211].realLife);
                            }
                        }

                        break;
                    }

                case 29:
                    {
                        int num161 = reader.ReadInt16();
                        int num162 = reader.ReadByte();

                        {
                            num162 = whoAmI;
                        }

                        for (int num163 = 0; num163 < 1000; num163++)
                        {
                            if (Main.projectile[num163].owner == num162 && Main.projectile[num163].identity == num161 && Main.projectile[num163].active)
                            {
                                Main.projectile[num163].Kill();
                                break;
                            }
                        }


                        {
                            NetMessage.TrySendData(29, -1, whoAmI, null, num161, num162);
                        }

                        break;
                    }

                case 30:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取是否敌对（PVP）
                        bool hostile = reader.ReadBoolean();
                        Main.player[playerIndex].hostile = hostile;

                        {
                            NetMessage.TrySendData(30, -1, whoAmI, null, playerIndex);
                            LocalizedText localizedText = (hostile ? Lang.mp[11] : Lang.mp[12]);
                            ChatHelper.BroadcastChatMessage(color: Main.teamColor[Main.player[playerIndex].team], text: NetworkText.FromKey(localizedText.Key, Main.player[playerIndex].name));
                        }

                        break;
                    }

                case 31:
                    {


                        // 读取箱子坐标X
                        int x = reader.ReadInt16();
                        // 读取箱子坐标Y
                        int y = reader.ReadInt16();
                        // 查找箱子索引
                        int chestIndex = Chest.FindChest(x, y);
                        if (chestIndex > -1 && Chest.UsingChest(chestIndex) == -1)
                        {
                            // 发送箱子内容给玩家
                            NetMessage.SendChestContentsTo(chestIndex, whoAmI);
                            NetMessage.TrySendData(33, whoAmI, -1, null, chestIndex);
                            Main.player[whoAmI].chest = chestIndex;
                            if (Main.myPlayer == whoAmI)
                            {
                                Main.PipsUseGrid = false;
                            }

                            NetMessage.TrySendData(80, -1, whoAmI, null, whoAmI, chestIndex);
                            if (Main.netMode == 2 && WorldGen.IsChestRigged(x, y))
                            {
                                Wiring.SetCurrentUser(whoAmI);
                                Wiring.HitSwitch(x, y);
                                Wiring.SetCurrentUser();
                                NetMessage.TrySendData(59, -1, whoAmI, null, x, y);
                            }
                        }

                        break;
                    }

                case 32:
                    {
                        // 读取箱子索引
                        int chestIndex = reader.ReadInt16();
                        // 读取物品槽索引
                        int itemIndex = reader.ReadByte();
                        // 读取堆叠数量
                        int stack = reader.ReadInt16();
                        // 读取前缀
                        int prefix = reader.ReadByte();
                        // 读取物品类型
                        int itemType = reader.ReadInt16();
                        if (chestIndex >= 0 && chestIndex < 8000 && Main.chest[chestIndex] != null)
                        {
                            if (Main.chest[chestIndex].item[itemIndex] == null)
                            {
                                Main.chest[chestIndex].item[itemIndex] = new Item();
                            }

                            Main.chest[chestIndex].item[itemIndex].SetDefaults(itemType);
                            Main.chest[chestIndex].item[itemIndex].Prefix(prefix);
                            Main.chest[chestIndex].item[itemIndex].stack = stack;

                            {
                                NetMessage.TrySendData(32, -1, whoAmI, null, chestIndex, itemIndex);
                            }
                        }

                        break;
                    }

                case 33:
                    {
                        // 读取箱子索引
                        int chestIndex = reader.ReadInt16();
                        // 读取箱子坐标X
                        int chestX = reader.ReadInt16();
                        // 读取箱子坐标Y
                        int chestY = reader.ReadInt16();
                        // 读取名字长度
                        int nameLen = reader.ReadByte();
                        string text = string.Empty;
                        if (nameLen != 0)
                        {
                            if (nameLen <= 20)
                            {
                                text = reader.ReadString();
                            }
                            else if (nameLen != 255)
                            {
                                nameLen = 0;
                            }
                        }


                        {
                            if (nameLen != 0)
                            {
                                int chest = Main.player[whoAmI].chest;
                                Chest chest2 = Main.chest[chest];
                                chest2.name = text;
                                NetMessage.TrySendData(69, -1, whoAmI, null, chest, chest2.x, chest2.y);
                            }

                            Main.player[whoAmI].chest = chestIndex;
                            NetMessage.TrySendData(80, -1, whoAmI, null, whoAmI, chestIndex);
                        }

                        break;
                    }

                case 34:
                    {
                        // 读取操作类型（0:放置箱子, 1:拆除箱子, 2:放置梳妆台, 3:拆除梳妆台, 4:放置箱子2, 5:拆除箱子2）
                        byte actionType = reader.ReadByte();
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 读取样式
                        int style = reader.ReadInt16();
                        // 读取箱子索引
                        int chestIndex = reader.ReadInt16();

                        {
                            chestIndex = 0;
                        }


                        {
                            switch (actionType)
                            {
                                case 0:
                                    {
                                        int placedChestIndex = WorldGen.PlaceChest(x, y, 21, notNearOtherChests: false, style);
                                        if (placedChestIndex == -1)
                                        {
                                            NetMessage.TrySendData(34, whoAmI, -1, null, actionType, x, y, style, placedChestIndex);
                                            int itemDrop = WorldGen.GetItemDrop_Chests(style, secondType: false);
                                            if (itemDrop > 0)
                                            {
                                                Item.NewItem(new EntitySource_TileBreak(x, y), x * 16, y * 16, 32, 32, itemDrop, 1, noBroadcast: true);
                                            }
                                        }
                                        else
                                        {
                                            NetMessage.TrySendData(34, -1, -1, null, actionType, x, y, style, placedChestIndex);
                                        }

                                        break;
                                    }

                                case 1:
                                    if (Main.tile[x, y].type == 21)
                                    {
                                        Tile tile = Main.tile[x, y];
                                        if (tile.frameX % 36 != 0)
                                        {
                                            x--;
                                        }

                                        if (tile.frameY % 36 != 0)
                                        {
                                            y--;
                                        }

                                        int chestId = Chest.FindChest(x, y);
                                        WorldGen.KillTile(x, y);
                                        if (!tile.active())
                                        {
                                            NetMessage.TrySendData(34, -1, -1, null, actionType, x, y, 0f, chestId);
                                        }

                                        break;
                                    }

                                    goto default;
                                default:
                                    switch (actionType)
                                    {
                                        case 2:
                                            {
                                                int placedChestIndex = WorldGen.PlaceChest(x, y, 88, notNearOtherChests: false, style);
                                                if (placedChestIndex == -1)
                                                {
                                                    NetMessage.TrySendData(34, whoAmI, -1, null, actionType, x, y, style, placedChestIndex);
                                                    Item.NewItem(new EntitySource_TileBreak(x, y), x * 16, y * 16, 32, 32, WorldGen.GetItemDrop_Dressers(style), 1, noBroadcast: true);
                                                }
                                                else
                                                {
                                                    NetMessage.TrySendData(34, -1, -1, null, actionType, x, y, style, placedChestIndex);
                                                }

                                                break;
                                            }

                                        case 3:
                                            if (Main.tile[x, y].type == 88)
                                            {
                                                Tile tile2 = Main.tile[x, y];
                                                x -= tile2.frameX % 54 / 18;
                                                if (tile2.frameY % 36 != 0)
                                                {
                                                    y--;
                                                }

                                                int chestId = Chest.FindChest(x, y);
                                                WorldGen.KillTile(x, y);
                                                if (!tile2.active())
                                                {
                                                    NetMessage.TrySendData(34, -1, -1, null, actionType, x, y, 0f, chestId);
                                                }

                                                break;
                                            }

                                            goto default;
                                        default:
                                            switch (actionType)
                                            {
                                                case 4:
                                                    {
                                                        int placedChestIndex = WorldGen.PlaceChest(x, y, 467, notNearOtherChests: false, style);
                                                        if (placedChestIndex == -1)
                                                        {
                                                            NetMessage.TrySendData(34, whoAmI, -1, null, actionType, x, y, style, placedChestIndex);
                                                            int itemDrop = WorldGen.GetItemDrop_Chests(style, secondType: true);
                                                            if (itemDrop > 0)
                                                            {
                                                                Item.NewItem(new EntitySource_TileBreak(x, y), x * 16, y * 16, 32, 32, itemDrop, 1, noBroadcast: true);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            NetMessage.TrySendData(34, -1, -1, null, actionType, x, y, style, placedChestIndex);
                                                        }

                                                        break;
                                                    }

                                                case 5:
                                                    if (Main.tile[x, y].type == 467)
                                                    {
                                                        Tile tile3 = Main.tile[x, y];
                                                        if (tile3.frameX % 36 != 0)
                                                        {
                                                            x--;
                                                        }

                                                        if (tile3.frameY % 36 != 0)
                                                        {
                                                            y--;
                                                        }

                                                        int chestId = Chest.FindChest(x, y);
                                                        WorldGen.KillTile(x, y);
                                                        if (!tile3.active())
                                                        {
                                                            NetMessage.TrySendData(34, -1, -1, null, actionType, x, y, 0f, chestId);
                                                        }
                                                    }

                                                    break;
                                            }

                                            break;
                                    }

                                    break;
                            }

                            break;
                        }

                        switch (actionType)
                        {
                            case 0:
                                if (chestIndex == -1)
                                {
                                    WorldGen.KillTile(x, y);
                                    break;
                                }

                                SoundEngine.PlaySound(0, x * 16, y * 16);
                                WorldGen.PlaceChestDirect(x, y, 21, style, chestIndex);
                                break;
                            case 2:
                                if (chestIndex == -1)
                                {
                                    WorldGen.KillTile(x, y);
                                    break;
                                }

                                SoundEngine.PlaySound(0, x * 16, y * 16);
                                WorldGen.PlaceDresserDirect(x, y, 88, style, chestIndex);
                                break;
                            case 4:
                                if (chestIndex == -1)
                                {
                                    WorldGen.KillTile(x, y);
                                    break;
                                }

                                SoundEngine.PlaySound(0, x * 16, y * 16);
                                WorldGen.PlaceChestDirect(x, y, 467, style, chestIndex);
                                break;
                            default:
                                Chest.DestroyChestDirect(x, y, chestIndex);
                                WorldGen.KillTile(x, y);
                                break;
                        }

                        break;
                    }

                case 35:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取回复量
                        int healAmount = reader.ReadInt16();
                        if (playerIndex != Main.myPlayer || Main.ServerSideCharacter)
                        {
                            Main.player[playerIndex].HealEffect(healAmount);
                        }


                        {
                            NetMessage.TrySendData(35, -1, whoAmI, null, playerIndex, healAmount);
                        }

                        break;
                    }

                case 36:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        playerIndex = whoAmI;

                        Player player = Main.player[playerIndex];
                        bool wasInZone5 = player.zone5[0]; // 记录之前的Zone5状态
                        player.zone1 = reader.ReadByte();
                        player.zone2 = reader.ReadByte();
                        player.zone3 = reader.ReadByte();
                        player.zone4 = reader.ReadByte();
                        player.zone5 = reader.ReadByte();
                        player.townNPCs = reader.ReadByte();
                        if (!wasInZone5 && player.zone5[0])
                        {
                            NPC.Spawner.SpawnFaelings(player);
                        }

                        NetMessage.TrySendData(36, -1, whoAmI, null, playerIndex);

                        break;
                    }

                case 37:


                    break;
                case 38:

                    {
                        if (reader.ReadString() == Netplay.ServerPassword)
                        {
                            Netplay.Clients[whoAmI].State = 1;
                            NetMessage.TrySendData(3, whoAmI);
                        }
                        else
                        {
                            NetMessage.TrySendData(2, whoAmI, -1, Lang.mp[1].ToNetworkText());
                        }
                    }

                    break;
                case 39:
                    {
                        // 读取物品索引
                        int itemIndex = reader.ReadInt16();
                        WorldItem item = Main.item[itemIndex];

                        if (item.playerIndexTheItemIsReservedFor == whoAmI)
                        {
                            item.playerIndexTheItemIsReservedFor = 255;
                            item.FindOwner();
                            if (item.playerIndexTheItemIsReservedFor == 255)
                            {
                                NetMessage.TrySendData(22, -1, whoAmI, null, itemIndex);
                            }
                        }

                        break;
                    }

                case 40:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取对话NPC索引
                        int npcIndex = reader.ReadInt16();
                        Main.player[playerIndex].SetTalkNPC(npcIndex);

                        {
                            NetMessage.TrySendData(40, -1, whoAmI, null, playerIndex);
                        }

                        break;
                    }

                case 41:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        Player player = Main.player[playerIndex];
                        // 读取物品旋转角度
                        float itemRotation = reader.ReadSingle();
                        // 读取物品动画帧
                        int itemAnimation = reader.ReadInt16();
                        player.itemRotation = itemRotation;
                        player.itemAnimation = itemAnimation;
                        player.channel = player.inventory[player.selectedItem].channel;

                        {
                            NetMessage.TrySendData(41, -1, whoAmI, null, playerIndex);
                        }

                        break;
                    }

                case 42:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }
                        // else if (Main.myPlayer == playerIndex && !Main.ServerSideCharacter)
                        // {
                        //     break;
                        // }

                        // 读取当前法力值
                        int mana = reader.ReadInt16();
                        // 读取最大法力值
                        int maxMana = reader.ReadInt16();
                        Main.player[playerIndex].statMana = mana;
                        Main.player[playerIndex].statManaMax = maxMana;
                        break;
                    }

                case 43:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取回复法力值量
                        int manaAmount = reader.ReadInt16();
                        if (playerIndex != Main.myPlayer)
                        {
                            Main.player[playerIndex].ManaEffect(manaAmount);
                        }


                        {
                            NetMessage.TrySendData(43, -1, whoAmI, null, playerIndex, manaAmount);
                        }

                        break;
                    }

                case 45:
                case 157:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取队伍ID
                        int teamId = reader.ReadByte();
                        Player player = Main.player[playerIndex];
                        int oldTeam = player.team;
                        player.team = teamId;
                        Color color = Main.teamColor[teamId];


                        NetMessage.TrySendData(45, -1, whoAmI, null, playerIndex);
                        LocalizedText localizedText = Lang.mp[13 + teamId];
                        if (teamId == 5)
                        {
                            localizedText = Lang.mp[22];
                        }

                        for (int i = 0; i < 255; i++)
                        {
                            if (i == whoAmI || (oldTeam > 0 && Main.player[i].team == oldTeam) || (teamId > 0 && Main.player[i].team == teamId))
                            {
                                ChatHelper.SendChatMessageToClient(NetworkText.FromKey(localizedText.Key, player.name), color, i);
                            }
                        }

                        if (b == 157 && Main.teamBasedSpawnsSeed)
                        {
                            Point spawnPoint = Point.Zero;
                            if (ExtraSpawnPointManager.TryGetExtraSpawnPointForTeam(teamId, out spawnPoint))
                            {
                                RemoteClient.CheckSection(whoAmI, spawnPoint.ToWorldCoordinates());
                                NetMessage.SendData(158, playerIndex, -1, null, playerIndex);
                            }
                        }

                        break;
                    }

                case 46:

                    {
                        // 读取坐标X
                        short x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 读取告示牌ID
                        int signIndex = Sign.ReadSign(x, y);
                        if (signIndex >= 0)
                        {
                            NetMessage.TrySendData(47, whoAmI, -1, null, signIndex, whoAmI);
                        }
                    }

                    break;
                case 47:
                    {
                        // 读取告示牌索引
                        int signIndex = reader.ReadInt16();
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 读取文本内容
                        string text = reader.ReadString();
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        BitsByte itemFlags = reader.ReadByte();
                        if (signIndex >= 0 && signIndex < 32000)
                        {
                            string oldText = null;
                            if (Main.sign[signIndex] != null)
                            {
                                oldText = Main.sign[signIndex].text;
                            }

                            Main.sign[signIndex] = new Sign();
                            Main.sign[signIndex].x = x;
                            Main.sign[signIndex].y = y;
                            Sign.TextSign(signIndex, text);
                            if (Main.netMode == 2 && oldText != text)
                            {
                                playerIndex = whoAmI;
                                NetMessage.TrySendData(47, -1, whoAmI, null, signIndex, playerIndex);
                            }


                        }

                        break;
                    }

                case 48:
                    {
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 读取液体量
                        byte liquidAmount = reader.ReadByte();
                        // 读取液体类型
                        byte liquidType = reader.ReadByte();
                        if (Main.netMode == 2 && Netplay.SpamCheck)
                        {
                            int playerIndex = whoAmI;
                            int playerCenterX = (int)(Main.player[playerIndex].position.X + (float)(Main.player[playerIndex].width / 2));
                            int playerCenterY = (int)(Main.player[playerIndex].position.Y + (float)(Main.player[playerIndex].height / 2));
                            int checkRadius = 10;
                            int minX = playerCenterX - checkRadius;
                            int maxX = playerCenterX + checkRadius;
                            int minY = playerCenterY - checkRadius;
                            int maxY = playerCenterY + checkRadius;
                            if (x < minX || x > maxX || y < minY || y > maxY)
                            {
                                Netplay.Clients[whoAmI].SpamWater += 1f;
                            }
                        }

                        if (Main.tile[x, y] == null)
                        {
                            Main.tile[x, y] = new Tile();
                        }

                        lock (Main.tile[x, y])
                        {
                            Main.tile[x, y].liquid = liquidAmount;
                            Main.tile[x, y].liquidType(liquidType);

                            {
                                WorldGen.SquareTileFrame(x, y);
                                if (liquidAmount == 0)
                                {
                                    NetMessage.SendData(48, -1, whoAmI, null, x, y);
                                }
                            }

                            break;
                        }
                    }

                case 49:
                    if (Netplay.Connection.State == 6)
                    {
                        Netplay.Connection.State = 10;
                        Main.player[Main.myPlayer].Spawn(PlayerSpawnContext.SpawningIntoWorld);
                    }

                    break;
                case 50:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }
                        // else if (playerIndex == Main.myPlayer && !Main.ServerSideCharacter)
                        // {
                        //     break;
                        // }

                        Player player = Main.player[playerIndex];
                        int buffIndex = 0;
                        int buffType;
                        while ((buffType = reader.ReadUInt16()) > 0)
                        {
                            player.buffType[buffIndex] = buffType;
                            player.buffTime[buffIndex] = 60;
                            buffIndex++;
                        }

                        Array.Clear(player.buffType, buffIndex, player.buffType.Length - buffIndex);
                        Array.Clear(player.buffTime, buffIndex, player.buffTime.Length - buffIndex);

                        {
                            NetMessage.TrySendData(50, -1, whoAmI, null, playerIndex);
                        }

                        break;
                    }

                case 51:
                    {
                        // 读取参数（通常是玩家索引）
                        byte param = reader.ReadByte();

                        {
                            param = (byte)whoAmI;
                        }

                        // 读取生成类型
                        byte spawnType = reader.ReadByte();
                        switch (spawnType)
                        {
                            case 1:
                                NPC.SpawnSkeletron(param);
                                break;
                            case 2:

                                {
                                    NetMessage.TrySendData(51, -1, whoAmI, null, param, (int)spawnType);
                                }
                                // else
                                // {
                                //     SoundEngine.PlaySound(SoundID.Item1, (int)Main.player[param].position.X, (int)Main.player[param].position.Y);
                                // }

                                break;
                            case 3:

                                {
                                    Main.Sundialing();
                                }

                                break;
                            case 4:
                                Main.npc[param].BigMimicSpawnSmoke();
                                break;
                            case 5:

                                {
                                    NPC mimic = new NPC();
                                    mimic.SetDefaults(664);
                                    Main.BestiaryTracker.Kills.RegisterKill(mimic);
                                }

                                break;
                            case 6:

                                {
                                    Main.Moondialing();
                                }

                                break;
                        }

                        break;
                    }

                case 52:
                    {
                        // 读取操作类型（1:解锁箱子, 2:解锁门, 3:锁住箱子）
                        int actionType = reader.ReadByte();
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        if (actionType == 1)
                        {
                            Chest.Unlock(x, y);

                            {
                                NetMessage.TrySendData(52, -1, whoAmI, null, 0, actionType, x, y);
                                NetMessage.SendTileSquare(-1, x, y, 2);
                            }
                        }

                        if (actionType == 2)
                        {
                            WorldGen.UnlockDoor(x, y);

                            {
                                NetMessage.TrySendData(52, -1, whoAmI, null, 0, actionType, x, y);
                                NetMessage.SendTileSquare(-1, x, y, 2);
                            }
                        }

                        if (actionType == 3)
                        {
                            Chest.Lock(x, y);

                            {
                                NetMessage.TrySendData(52, -1, whoAmI, null, 0, actionType, x, y);
                                NetMessage.SendTileSquare(-1, x, y, 2);
                            }
                        }

                        break;
                    }

                case 53:
                    {
                        // 读取NPC索引
                        int npcIndex = reader.ReadInt16();
                        // 读取Buff类型
                        int buffType = reader.ReadUInt16();
                        // 读取持续时间
                        int duration = reader.ReadInt16();
                        Main.npc[npcIndex].AddBuff(buffType, duration, quiet: true);

                        {
                            NetMessage.TrySendData(54, -1, -1, null, npcIndex);
                        }

                        break;
                    }

                case 54:

                    break;
                case 55:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        // 读取Buff类型
                        int buffType = reader.ReadUInt16();
                        // 读取持续时间
                        int duration = reader.ReadInt32();
                        if ((Main.netMode != 2 || Main.pvpBuff[buffType]) && (Main.netMode != 1 || playerIndex == Main.myPlayer))
                        {

                            {
                                NetMessage.TrySendData(55, playerIndex, -1, null, playerIndex, buffType, duration);
                            }
                            // else
                            // {
                            //     Main.player[playerIndex].AddBuff(buffType, duration, fromNetPvP: true);
                            // }
                        }

                        break;
                    }

                case 56:
                    {
                        // 读取NPC索引
                        int npcIndex = reader.ReadInt16();
                        if (npcIndex >= 0 && npcIndex < Main.maxNPCs)
                        {


                            {
                                NetMessage.TrySendData(56, whoAmI, -1, null, npcIndex);
                            }
                        }

                        break;
                    }

                case 57:


                    break;
                case 58:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        // 服务器逻辑：重置玩家索引

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取音调
                        float pitch = reader.ReadSingle();
                        // 服务器逻辑：转发消息

                        {
                            NetMessage.TrySendData(58, -1, whoAmI, null, whoAmI, pitch);
                            break;
                        }

                        // 获取玩家实例
                        Player player = Main.player[playerIndex];
                        // 获取选中物品类型
                        int itemType = player.inventory[player.selectedItem].type;
                        switch (itemType)
                        {
                            case 4057:
                            case 4372:
                            case 4715:
                                // 播放吉他和弦
                                player.PlayGuitarChord(pitch);
                                break;
                            case 4673:
                                // 播放鼓声
                                player.PlayDrums(pitch);
                                break;
                            default:
                                {
                                    // 设置音乐音调
                                    Main.musicPitch = pitch;
                                    // 默认音效样式
                                    LegacySoundStyle soundStyle = SoundID.Item26;
                                    // 特殊物品音效
                                    if (itemType == 507)
                                    {
                                        soundStyle = SoundID.Item35;
                                    }

                                    if (itemType == 1305)
                                    {
                                        soundStyle = SoundID.Item47;
                                    }

                                    // 播放音效
                                    SoundEngine.PlaySound(soundStyle, player.position);
                                    break;
                                }
                        }

                        break;
                    }

                case 59:
                    {
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 设置当前电路触发者
                        Wiring.SetCurrentUser(whoAmI);
                        // 触发开关
                        Wiring.HitSwitch(x, y);
                        // 重置当前电路触发者
                        Wiring.SetCurrentUser();
                        // 服务器逻辑：转发消息

                        {
                            NetMessage.TrySendData(59, -1, whoAmI, null, x, y);
                        }

                        break;
                    }

                case 60:
                    {
                        // 读取NPC索引
                        int npcIndex = reader.ReadInt16();
                        // 读取家坐标X
                        int homeX = reader.ReadInt16();
                        // 读取家坐标Y
                        int homeY = reader.ReadInt16();
                        // 读取操作类型（1:无家可归/踢出, 2:设置房间）
                        byte actionType = reader.ReadByte();
                        // 校验NPC索引
                        if (npcIndex >= Main.maxNPCs)
                        {
                            NetMessage.BootPlayer(whoAmI, NetworkText.FromKey("Net.CheatingInvalid"));
                            break;
                        }

                        // 获取NPC实例
                        NPC npc = Main.npc[npcIndex];
                        // 是否为城镇NPC
                        bool isLikeATownNPC = npc.isLikeATownNPC;
                        // 客户端逻辑：更新NPC无家可归状态和家坐标


                        if (!isLikeATownNPC)
                        {
                            break;
                        }

                        // 服务器逻辑：处理踢出
                        if (actionType == 1)
                        {
                            WorldGen.kickOut(npcIndex);
                        }
                        // 服务器逻辑：处理移动房间
                        else
                        {
                            WorldGen.moveRoom(homeX, homeY, npcIndex);
                        }

                        break;
                    }

                case 61:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadInt16();
                        // 读取生成类型（NPC ID或事件ID）
                        int spawnType = reader.ReadInt16();
                        // 仅服务器处理

                        // 如果是合法NPC ID，在玩家处生成
                        if (spawnType >= 0 && spawnType < NPCID.Count && NPCID.Sets.MPAllowedEnemies[spawnType])
                        {
                            if (!NPC.AnyNPCs(spawnType))
                            {
                                NPC.SpawnOnPlayer(playerIndex, spawnType);
                            }
                        }
                        // 特殊事件ID处理
                        else if (spawnType == -4)
                        {
                            // 南瓜月
                            if (!Main.dayTime && !DD2Event.Ongoing)
                            {
                                ChatHelper.BroadcastChatMessage(NetworkText.FromKey(Lang.misc[31].Key), new Color(50, 255, 130));
                                Main.startPumpkinMoon();
                                NetMessage.TrySendData(7);
                                NetMessage.TrySendData(78, -1, -1, null, 0, 1f, 2f, 1f);
                            }
                        }
                        else if (spawnType == -5)
                        {
                            // 霜月
                            if (!Main.dayTime && !DD2Event.Ongoing)
                            {
                                ChatHelper.BroadcastChatMessage(NetworkText.FromKey(Lang.misc[34].Key), new Color(50, 255, 130));
                                Main.startSnowMoon();
                                NetMessage.TrySendData(7);
                                NetMessage.TrySendData(78, -1, -1, null, 0, 1f, 1f, 1f);
                            }
                        }
                        else if (spawnType == -6)
                        {
                            // 日食
                            if (Main.dayTime && !Main.eclipse)
                            {
                                if (Main.remixWorld)
                                {
                                    ChatHelper.BroadcastChatMessage(NetworkText.FromKey(Lang.misc[106].Key), new Color(50, 255, 130));
                                }
                                else
                                {
                                    ChatHelper.BroadcastChatMessage(NetworkText.FromKey(Lang.misc[20].Key), new Color(50, 255, 130));
                                }

                                Main.eclipse = true;
                                NetMessage.TrySendData(7);
                            }
                        }
                        else if (spawnType == -7)
                        {
                            // 入侵
                            Main.invasionDelay = 0;
                            Main.StartInvasion(4);
                            NetMessage.TrySendData(7);
                            NetMessage.TrySendData(78, -1, -1, null, 0, 1f, Main.invasionType + 3);
                        }
                        else if (spawnType == -8)
                        {
                            // 毁灭前兆（天界柱）
                            if (NPC.downedGolemBoss && Main.hardMode && !NPC.AnyDanger() && !NPC.AnyoneNearCultists())
                            {
                                WorldGen.StartImpendingDoom(720);
                                NetMessage.TrySendData(7);
                            }
                        }
                        else if (spawnType == -10)
                        {
                            // 血月
                            if (!Main.dayTime && !Main.bloodMoon)
                            {
                                ChatHelper.BroadcastChatMessage(NetworkText.FromKey(Lang.misc[8].Key), new Color(50, 255, 130));
                                Main.bloodMoon = true;
                                if (Main.GetMoonPhase() == MoonPhase.Empty)
                                {
                                    Main.moonPhase = 5;
                                }

                                AchievementsHelper.NotifyProgressionEvent(4);
                                NetMessage.TrySendData(7);
                            }
                        }
                        else if (spawnType == -11)
                        {
                            // 战斗手册
                            ChatHelper.BroadcastChatMessage(NetworkText.FromKey("Misc.CombatBookUsed"), new Color(50, 255, 130));
                            NPC.combatBookWasUsed = true;
                            NetMessage.TrySendData(7);
                        }
                        else if (spawnType == -12)
                        {
                            // 猫许可证
                            NPC.UnlockOrExchangePet(ref NPC.boughtCat, 637, "Misc.LicenseCatUsed", spawnType);
                        }
                        else if (spawnType == -13)
                        {
                            // 狗许可证
                            NPC.UnlockOrExchangePet(ref NPC.boughtDog, 638, "Misc.LicenseDogUsed", spawnType);
                        }
                        else if (spawnType == -14)
                        {
                            // 兔许可证
                            NPC.UnlockOrExchangePet(ref NPC.boughtBunny, 656, "Misc.LicenseBunnyUsed", spawnType);
                        }
                        else if (spawnType == -15)
                        {
                            // 史莱ム许可证
                            NPC.UnlockOrExchangePet(ref NPC.unlockedSlimeBlueSpawn, 670, "Misc.LicenseSlimeUsed", spawnType);
                        }
                        else if (spawnType == -16)
                        {
                            // 机械美杜莎
                            NPC.SpawnMechQueen(playerIndex);
                        }
                        else if (spawnType == -17)
                        {
                            // 战斗手册卷二
                            ChatHelper.BroadcastChatMessage(NetworkText.FromKey("Misc.CombatBookVolumeTwoUsed"), new Color(50, 255, 130));
                            NPC.combatBookVolumeTwoWasUsed = true;
                            NetMessage.TrySendData(7);
                        }
                        else if (spawnType == -18)
                        {
                            // 商贩背包
                            ChatHelper.BroadcastChatMessage(NetworkText.FromKey("Misc.PeddlersSatchelUsed"), new Color(50, 255, 130));
                            NPC.peddlersSatchelWasUsed = true;
                            NetMessage.TrySendData(7);
                        }
                        else if (spawnType == -19)
                        {
                            // 史莱ム雨
                            Main.StartSlimeRain();
                        }
                        else if (spawnType < 0)
                        {
                            // 其他入侵事件
                            int invasionId = 1;
                            if (spawnType > -InvasionID.Count)
                            {
                                invasionId = -spawnType;
                            }

                            if (invasionId > 0 && Main.invasionType == 0)
                            {
                                Main.invasionDelay = 0;
                                Main.StartInvasion(invasionId);
                            }

                            NetMessage.TrySendData(7);
                            NetMessage.TrySendData(78, -1, -1, null, 0, 1f, Main.invasionType + 3);
                        }

                        break;
                    }

                case 62:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        // 读取闪避类型
                        int dodgeType = reader.ReadByte();
                        // 服务器逻辑：重置玩家索引

                        {
                            playerIndex = whoAmI;
                        }

                        // 忍者闪避
                        if (dodgeType == 1)
                        {
                            Main.player[playerIndex].NinjaDodge();
                        }

                        // 暗影闪避
                        if (dodgeType == 2)
                        {
                            Main.player[playerIndex].ShadowDodge();
                        }

                        // 混乱之脑闪避
                        if (dodgeType == 4)
                        {
                            Main.player[playerIndex].BrainOfConfusionDodge();
                        }

                        // 服务器逻辑：转发消息

                        {
                            NetMessage.TrySendData(62, -1, whoAmI, null, playerIndex, dodgeType);
                        }

                        break;
                    }

                case 63:
                    {
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 读取油漆类型
                        byte paintType = reader.ReadByte();
                        // 读取涂层类型
                        byte coating = reader.ReadByte();
                        // 应用油漆或涂层
                        if (coating == 0)
                        {
                            WorldGen.paintTile(x, y, paintType);
                        }
                        else
                        {
                            WorldGen.paintCoatTile(x, y, paintType);
                        }

                        // 服务器逻辑：转发消息

                        {
                            NetMessage.TrySendData(63, -1, whoAmI, null, x, y, (int)paintType, (int)coating);
                        }

                        break;
                    }

                case 64:
                    {
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 读取油漆类型
                        byte paintType = reader.ReadByte();
                        // 读取涂层类型
                        byte coating = reader.ReadByte();
                        // 应用墙壁油漆或涂层
                        if (coating == 0)
                        {
                            WorldGen.paintWall(x, y, paintType);
                        }
                        else
                        {
                            WorldGen.paintCoatWall(x, y, paintType);
                        }

                        // 服务器逻辑：转发消息

                        {
                            NetMessage.TrySendData(64, -1, whoAmI, null, x, y, (int)paintType, (int)coating);
                        }

                        break;
                    }

                case 65:
                    {
                        // 读取标志位
                        BitsByte flags = reader.ReadByte();
                        // 读取目标索引
                        int targetIndex = reader.ReadInt16();
                        // 服务器逻辑：重置目标索引为发送者

                        {
                            targetIndex = whoAmI;
                        }

                        // 读取位置
                        Vector2 position = reader.ReadVector2();
                        int style = 0;
                        // 读取传送样式
                        style = reader.ReadByte();
                        // 解析传送类型
                        int teleportType = 0;
                        if (flags[0])
                        {
                            teleportType++;
                        }

                        if (flags[1])
                        {
                            teleportType += 2;
                        }

                        // 解析是否传送到玩家
                        bool teleportToPlayer = false;
                        if (flags[2])
                        {
                            teleportToPlayer = true;
                        }

                        // 解析额外信息
                        int extraInfo = 0;
                        if (flags[3])
                        {
                            extraInfo = reader.ReadInt32();
                        }

                        // 如果传送到玩家，更新位置
                        if (teleportToPlayer)
                        {
                            position = Main.player[targetIndex].position;
                        }

                        switch (teleportType)
                        {
                            case 0:
                                // 玩家传送
                                Main.player[targetIndex].Teleport(position, style, extraInfo);
                                // 服务器逻辑：同步给其他玩家

                                {
                                    NetMessage.TrySendData(65, -1, whoAmI, null, 0, targetIndex, position.X, position.Y, style, teleportToPlayer.ToInt(), extraInfo);
                                }

                                // 客户端逻辑：确认传送


                                break;
                            case 1:
                                // NPC传送
                                Main.npc[targetIndex].Teleport(position, style, extraInfo);
                                Main.npc[targetIndex].netOffset *= 0f;
                                break;
                            case 2:
                                {
                                    // 玩家传送（带检查）
                                    Main.player[targetIndex].Teleport(position, style, extraInfo);


                                    // 检查区块加载
                                    RemoteClient.CheckSection(whoAmI, position);
                                    // 同步传送
                                    NetMessage.TrySendData(65, -1, -1, null, 0, targetIndex, position.X, position.Y, style, teleportToPlayer.ToInt(), extraInfo);

                                    // 查找最近的玩家以发送广播消息
                                    int closestPlayerIndex = -1;
                                    float minDistance = 9999f;
                                    for (int i = 0; i < 255; i++)
                                    {
                                        if (Main.player[i].active && i != whoAmI)
                                        {
                                            Vector2 diff = Main.player[i].position - Main.player[whoAmI].position;
                                            if (diff.Length() < minDistance)
                                            {
                                                minDistance = diff.Length();
                                                closestPlayerIndex = i;
                                            }
                                        }
                                    }

                                    if (closestPlayerIndex >= 0)
                                    {
                                        ChatHelper.BroadcastChatMessage(NetworkText.FromKey("Game.HasTeleportedTo", Main.player[whoAmI].name, Main.player[closestPlayerIndex].name), new Color(250, 250, 0));
                                    }

                                    break;
                                }

                            case 3:
                                // 减少未确认的传送计数
                                Main.player[targetIndex].unacknowledgedTeleports--;
                                break;
                        }

                        break;
                    }

                case 66:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        // 读取治疗量
                        int healAmount = reader.ReadInt16();
                        if (healAmount > 0)
                        {
                            // 获取玩家实例
                            Player player = Main.player[playerIndex];
                            player.statLife += healAmount;
                            // 限制最大生命值
                            if (player.statLife > player.statLifeMax2)
                            {
                                player.statLife = player.statLifeMax2;
                            }

                            // 播放治疗特效
                            player.HealEffect(healAmount, broadcast: false);
                            // 服务器逻辑：转发消息

                            {
                                NetMessage.TrySendData(66, -1, whoAmI, null, playerIndex, healAmount);
                            }
                        }

                        break;
                    }

                case 68:
                    // 读取UUID（仅消耗，未使用）
                    reader.ReadString();
                    break;
                case 69:
                    {
                        // 读取箱子索引
                        int chestIndex = reader.ReadInt16();
                        // 读取坐标X
                        int x = reader.ReadInt16();
                        // 读取坐标Y
                        int y = reader.ReadInt16();
                        // 客户端逻辑：更新箱子名称

                        // 服务器逻辑：处理箱子名称请求或更新

                        {
                            if (chestIndex < -1 || chestIndex >= 8000)
                            {
                                break;
                            }

                            // 请求查找箱子
                            if (chestIndex == -1)
                            {
                                chestIndex = Chest.FindChest(x, y);
                                if (chestIndex == -1)
                                {
                                    break;
                                }
                            }

                            Chest chest = Main.chest[chestIndex];
                            if (chest.x == x && chest.y == y)
                            {
                                NetMessage.TrySendData(69, whoAmI, -1, null, chestIndex, x, y);
                            }
                        }

                        break;
                    }

                case 70:
                    // 服务器逻辑：捕捉NPC

                    {
                        int npcIndex = reader.ReadInt16();
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        if (npcIndex < Main.maxNPCs && npcIndex >= 0)
                        {
                            NPC.CatchNPC(npcIndex, playerIndex);
                        }
                    }

                    break;
                case 71:
                    // 服务器逻辑：释放NPC

                    {
                        int x = reader.ReadInt32();
                        int y = reader.ReadInt32();
                        int npcType = reader.ReadInt16();
                        byte style = reader.ReadByte();
                        NPC.ReleaseNPC(x, y, npcType, style, whoAmI);
                    }

                    break;
                case 72:
                    // 客户端逻辑：更新旅商商店


                    break;
                case 73:
                    // 传送物品效果
                    switch (reader.ReadByte())
                    {
                        case 0:
                            // 传送药水
                            Main.player[whoAmI].TeleportationPotion();
                            break;
                        case 1:
                            // 魔法海螺
                            Main.player[whoAmI].MagicConch();
                            break;
                        case 2:
                            // 恶魔海螺
                            Main.player[whoAmI].DemonConch();
                            break;
                        case 3:
                            // 贝壳电话（出生点）
                            Main.player[whoAmI].Shellphone_Spawn();
                            break;
                        case 4:
                            // 玩家无空间传送
                            Main.player[whoAmI].PlayerNoSpaceTeleport();
                            break;
                    }

                    break;
                case 74:
                    // 客户端逻辑：更新渔夫任务

                    break;
                case 75:
                    // 服务器逻辑：记录今日完成渔夫任务的玩家

                    {
                        string playerName = Main.player[whoAmI].name;
                        if (!Main.anglerWhoFinishedToday.Contains(playerName))
                        {
                            Main.anglerWhoFinishedToday.Add(playerName);
                        }
                    }

                    break;
                case 76:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        if (playerIndex != Main.myPlayer || Main.ServerSideCharacter)
                        {
                            // 服务器逻辑：重置玩家索引

                            {
                                playerIndex = whoAmI;
                            }

                            // 更新玩家渔夫任务和高尔夫分数
                            Player player = Main.player[playerIndex];
                            player.anglerQuestsFinished = reader.ReadInt32();
                            player.golferScoreAccumulated = reader.ReadInt32();
                            // 服务器逻辑：转发消息

                            {
                                NetMessage.TrySendData(76, -1, whoAmI, null, playerIndex);
                            }
                        }

                        break;
                    }

                case 77:
                    {
                        // 读取动画类型
                        short animationType = reader.ReadInt16();
                        // 读取图格类型
                        ushort tileType = reader.ReadUInt16();
                        // 读取坐标X
                        short x = reader.ReadInt16();
                        // 读取坐标Y
                        short y = reader.ReadInt16();
                        // 创建临时动画
                        Animation.NewTemporaryAnimation(animationType, tileType, x, y);
                        break;
                    }

                case 78:
                    // 客户端逻辑：更新入侵进度


                    break;
                case 79:
                    {
                        int x13 = reader.ReadInt16();
                        int y13 = reader.ReadInt16();
                        short type18 = reader.ReadInt16();
                        int style2 = reader.ReadInt16();
                        int num204 = reader.ReadByte();
                        int random = reader.ReadSByte();
                        int direction = (reader.ReadBoolean() ? 1 : (-1));

                        {
                            Netplay.Clients[whoAmI].SpamAddBlock += 1f;
                            if (!WorldGen.InWorld(x13, y13, 10) || !Netplay.Clients[whoAmI].TileSections[Netplay.GetSectionX(x13), Netplay.GetSectionY(y13)])
                            {
                                break;
                            }
                        }

                        WorldGen.PlaceObject(x13, y13, type18, mute: false, style2, num204, random, direction);

                        {
                            NetMessage.SendObjectPlacement(whoAmI, x13, y13, type18, style2, num204, random, direction);
                        }

                        break;
                    }

                case 80:


                    break;
                case 81:


                    break;
                case 119:


                    break;
                case 82:
                    NetManager.Instance.Read(reader, whoAmI, length);
                    break;
                case 84:
                    {
                        int num165 = reader.ReadByte();

                        {
                            num165 = whoAmI;
                        }

                        float stealth = reader.ReadSingle();
                        Main.player[num165].stealth = stealth;

                        {
                            NetMessage.TrySendData(84, -1, whoAmI, null, num165);
                        }

                        break;
                    }

                case 85:
                    if (Main.netMode == 2 && whoAmI < 255)
                    {
                        Player player16 = Main.player[whoAmI];
                        QuickStacking.SourceInventory inventory = QuickStacking.ReadNetInventory(player16, reader);
                        bool smartStack = reader.ReadBoolean();
                        QuickStacking.QuickStackToNearbyChests(player16, inventory, smartStack);
                    }


                    break;
                case 86:
                    {


                        break;
                    }

                case 87:

                    {
                        int x10 = reader.ReadInt16();
                        int y10 = reader.ReadInt16();
                        int type14 = reader.ReadByte();
                        if (WorldGen.InWorld(x10, y10) && !TileEntity.TryGetAt<TileEntity>(x10, y10, out var _))
                        {
                            TileEntity.PlaceEntityNet(x10, y10, type14);
                        }
                    }

                    break;
                case 88:
                    {

                        break;
                    }

                case 89:
                    // 服务器逻辑：放置物品展示框

                    {
                        short x = reader.ReadInt16();
                        int y = reader.ReadInt16();
                        int type = reader.ReadInt16();
                        int prefix = reader.ReadByte();
                        int stack = reader.ReadInt16();
                        TEItemFrame.TryPlacing(x, y, type, prefix, stack);
                    }

                    break;
                case 91:
                    {
                        break;
                    }

                case 92:
                    {
                        // 读取NPC索引
                        int npcIndex = reader.ReadInt16();
                        // 读取数值（价值）
                        int value = reader.ReadInt32();
                        // 读取坐标X
                        float x = reader.ReadSingle();
                        // 读取坐标Y
                        float y = reader.ReadSingle();
                        if (npcIndex >= 0 && npcIndex <= Main.maxNPCs)
                        {
                            Main.npc[npcIndex].extraValue += value;
                            NetMessage.TrySendData(92, -1, -1, null, npcIndex, Main.npc[npcIndex].extraValue, x, y);
                        }

                        break;
                    }

                case 94:
                    {
                        // 读取命令
                        string command = reader.ReadString();
                        reader.ReadInt32();
                        // 读取参数
                        int arg1 = (int)reader.ReadSingle();
                        reader.ReadSingle();
                        if (DebugOptions.enableDebugCommands)
                        {
                            if (command == "/showdebug")
                            {
                                DebugOptions.Shared_ReportCommandUsage = arg1 == 1;
                            }
                            else if (command == "/setserverping")
                            {
                                DebugOptions.Shared_ServerPing = arg1;
                                DebugNetworkStream.Latency = (uint)(arg1 / 2);
                            }
                        }

                        break;
                    }

                case 95:
                    {
                        // 读取所有者索引
                        ushort ownerIndex = reader.ReadUInt16();
                        // 读取弹幕标识
                        int identity = reader.ReadByte();
                        // 仅服务器处理

                        // 遍历弹幕查找匹配项并击杀
                        for (int projIndex = 0; projIndex < 1000; projIndex++)
                        {
                            if (Main.projectile[projIndex].owner == ownerIndex && Main.projectile[projIndex].active && Main.projectile[projIndex].type == 602 && Main.projectile[projIndex].ai[1] == (float)identity)
                            {
                                Main.projectile[projIndex].Kill();
                                NetMessage.TrySendData(29, -1, -1, null, Main.projectile[projIndex].identity, (int)ownerIndex);
                                break;
                            }
                        }

                        break;
                    }

                case 96:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 获取玩家实例
                        Player player = Main.player[playerIndex];
                        // 读取传送门颜色索引
                        int portalColorIndex = reader.ReadInt16();
                        // 读取新位置
                        Vector2 newPos = reader.ReadVector2();
                        // 读取速度
                        Vector2 velocity = reader.ReadVector2();
                        // 计算上一次传送门颜色索引
                        int lastPortalColorIndex = portalColorIndex + ((portalColorIndex % 2 == 0) ? 1 : (-1));
                        player.lastPortalColorIndex = lastPortalColorIndex;
                        // 执行传送
                        player.Teleport(newPos, 4, portalColorIndex);
                        player.velocity = velocity;
                        // 如果是服务器，转发消息

                        {
                            NetMessage.SendData(96, -1, playerIndex, null, playerIndex, newPos.X, newPos.Y, portalColorIndex);
                        }

                        break;
                    }

                case 97:

                    break;
                case 98:


                    break;
                case 99:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 设置召唤物休息目标点
                        Main.player[playerIndex].MinionRestTargetPoint = reader.ReadVector2();
                        // 如果是服务器，转发消息

                        {
                            NetMessage.TrySendData(99, -1, whoAmI, null, playerIndex);
                        }

                        break;
                    }

                case 115:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 设置召唤物攻击目标NPC
                        Main.player[playerIndex].MinionAttackTargetNPC = reader.ReadInt16();
                        // 如果是服务器，转发消息

                        {
                            NetMessage.TrySendData(115, -1, whoAmI, null, playerIndex);
                        }

                        break;
                    }

                case 100:
                    {
                        // 读取NPC索引
                        int npcIndex = reader.ReadUInt16();
                        NPC npc = Main.npc[npcIndex];
                        // 读取传送门颜色索引
                        int portalColorIndex = reader.ReadInt16();
                        // 读取新位置
                        Vector2 newPos = reader.ReadVector2();
                        // 读取速度
                        Vector2 velocity = reader.ReadVector2();
                        // 计算上一次传送门颜色索引
                        int lastPortalColorIndex = portalColorIndex + ((portalColorIndex % 2 == 0) ? 1 : (-1));
                        npc.lastPortalColorIndex = lastPortalColorIndex;
                        // 执行传送
                        npc.Teleport(newPos, 4, portalColorIndex);
                        npc.velocity = velocity;
                        npc.netOffset *= 0f;
                        break;
                    }

                case 101:
                    // 仅非服务器处理

                    // 读取各塔护盾强度
                    NPC.ShieldStrengthTowerSolar = reader.ReadUInt16();
                    NPC.ShieldStrengthTowerVortex = reader.ReadUInt16();
                    NPC.ShieldStrengthTowerNebula = reader.ReadUInt16();
                    NPC.ShieldStrengthTowerStardust = reader.ReadUInt16();
                    // 确保护盾强度在合法范围内
                    if (NPC.ShieldStrengthTowerSolar < 0)
                    {
                        NPC.ShieldStrengthTowerSolar = 0;
                    }

                    if (NPC.ShieldStrengthTowerVortex < 0)
                    {
                        NPC.ShieldStrengthTowerVortex = 0;
                    }

                    if (NPC.ShieldStrengthTowerNebula < 0)
                    {
                        NPC.ShieldStrengthTowerNebula = 0;
                    }

                    if (NPC.ShieldStrengthTowerStardust < 0)
                    {
                        NPC.ShieldStrengthTowerStardust = 0;
                    }

                    if (NPC.ShieldStrengthTowerSolar > NPC.LunarShieldPowerMax)
                    {
                        NPC.ShieldStrengthTowerSolar = NPC.LunarShieldPowerMax;
                    }

                    if (NPC.ShieldStrengthTowerVortex > NPC.LunarShieldPowerMax)
                    {
                        NPC.ShieldStrengthTowerVortex = NPC.LunarShieldPowerMax;
                    }

                    if (NPC.ShieldStrengthTowerNebula > NPC.LunarShieldPowerMax)
                    {
                        NPC.ShieldStrengthTowerNebula = NPC.LunarShieldPowerMax;
                    }

                    if (NPC.ShieldStrengthTowerStardust > NPC.LunarShieldPowerMax)
                    {
                        NPC.ShieldStrengthTowerStardust = NPC.LunarShieldPowerMax;
                    }

                    break;
                case 102:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        // 读取buff类型
                        ushort buffType = reader.ReadUInt16();
                        // 读取位置
                        Vector2 position = reader.ReadVector2();
                        // 如果是服务器，转发消息

                        {
                            playerIndex = whoAmI;
                            NetMessage.TrySendData(102, -1, -1, null, playerIndex, (int)buffType, position.X, position.Y);
                            break;
                        }

                        // 获取源玩家
                        Player sourcePlayer = Main.player[playerIndex];
                        // 遍历所有玩家
                        for (int targetPlayerIndex = 0; targetPlayerIndex < 255; targetPlayerIndex++)
                        {
                            Player targetPlayer = Main.player[targetPlayerIndex];
                            // 检查目标玩家是否有效且在范围内
                            if (!targetPlayer.active || targetPlayer.dead || (sourcePlayer.team != 0 && sourcePlayer.team != targetPlayer.team) || !(targetPlayer.Distance(position) < 700f))
                            {
                                continue;
                            }

                            // 计算特效向量
                            Vector2 offset = sourcePlayer.Center - targetPlayer.Center;
                            Vector2 direction = Vector2.Normalize(offset);
                            if (!direction.HasNaNs())
                            {
                                int dustType = 90;
                                float rotation = 0f;
                                float rotationStep = (float)Math.PI / 15f;
                                Vector2 spinningPoint = new Vector2(0f, -8f);
                                Vector2 dustOffset = new Vector2(-3f);
                                float fadeIn = 0f;
                                float fadeInStep = 0.005f;
                                switch (buffType)
                                {
                                    case 179:
                                        dustType = 86;
                                        break;
                                    case 173:
                                        dustType = 90;
                                        break;
                                    case 176:
                                        dustType = 88;
                                        break;
                                }

                                // 生成特效
                                for (int i = 0; (float)i < offset.Length() / 6f; i++)
                                {
                                    Vector2 dustPosition = targetPlayer.Center + 6f * (float)i * direction + spinningPoint.RotatedBy(rotation) + dustOffset;
                                    rotation += rotationStep;
                                    int dustIndex = Dust.NewDust(dustPosition, 6, 6, dustType, 0f, 0f, 100, default(Color), 1.5f);
                                    Main.dust[dustIndex].noGravity = true;
                                    Main.dust[dustIndex].velocity = Vector2.Zero;
                                    fadeIn = (Main.dust[dustIndex].fadeIn = fadeIn + fadeInStep);
                                    Main.dust[dustIndex].velocity += direction * 1.5f;
                                }
                            }

                            // 玩家获得星云buff升级
                            targetPlayer.NebulaLevelup(buffType);
                        }

                        break;
                    }

                case 103:
                    // 仅客户端处理


                    break;
                case 104:
                    break;
                case 105:
                    // 仅非客户端处理
                    // 读取坐标
                    short x = reader.ReadInt16();
                    int y = reader.ReadInt16();
                    // 读取状态
                    bool on = reader.ReadBoolean();
                    // 切换宝石锁状态
                    WorldGen.ToggleGemLock(x, y, on);

                    break;
                case 106:


                    break;
                case 107:


                    break;
                case 108:

                    break;
                case 109:
                    // 仅服务器处理

                    {
                        // 读取起点和终点坐标
                        short startX = reader.ReadInt16();
                        int startY = reader.ReadInt16();
                        int endX = reader.ReadInt16();
                        int endY = reader.ReadInt16();
                        // 读取工具模式
                        byte toolModeByte = reader.ReadByte();
                        int playerIndex = whoAmI;
                        // 保存旧的工具模式
                        WiresUI.Settings.MultiToolMode oldToolMode = WiresUI.Settings.ToolMode;
                        // 设置新的工具模式
                        WiresUI.Settings.ToolMode = (WiresUI.Settings.MultiToolMode)toolModeByte;
                        // 执行大规模电路操作
                        Wiring.MassWireOperation(new Point(startX, startY), new Point(endX, endY), Main.player[playerIndex]);
                        // 恢复旧的工具模式
                        WiresUI.Settings.ToolMode = oldToolMode;
                    }

                    break;
                case 110:
                    {

                        break;
                    }

                case 111:
                    // 仅服务器处理

                    {
                        // 切换手动派对状态
                        BirthdayParty.ToggleManualParty();
                    }

                    break;
                case 112:
                    {
                        // 读取特效类型
                        int effectType = reader.ReadByte();
                        // 读取坐标和参数
                        int x = reader.ReadInt32();
                        int y = reader.ReadInt32();
                        int param1 = reader.ReadByte();
                        int param2 = reader.ReadInt16();
                        // 读取标志位
                        bool flag = reader.ReadByte() == 1;
                        switch (effectType)
                        {
                            case 1:



                                {
                                    // 转发消息
                                    NetMessage.TrySendData(112, -1, -1, null, effectType, x, y, param1, param2, flag ? 1 : 0);
                                }

                                break;
                            case 2:
                                // 播放仙灵特效
                                NPC.FairyEffects(new Vector2(x, y), param2);
                                break;
                        }

                        break;
                    }

                case 113:
                    {
                        // 读取坐标
                        int x = reader.ReadInt16();
                        int y = reader.ReadInt16();
                        // 仅服务器且非霜月/南瓜月事件时处理
                        if (Main.netMode == 2 && !Main.snowMoon && !Main.pumpkinMoon)
                        {
                            // 检查是否无法生成
                            if (DD2Event.WouldFailSpawningHere(x, y))
                            {
                                DD2Event.FailureMessage(whoAmI);
                            }

                            // 召唤水晶
                            DD2Event.SummonCrystal(x, y, whoAmI);
                        }

                        break;
                    }

                case 114:
                    // 仅客户端处理


                    break;
                case 116:


                    break;
                case 117:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();
                        // 验证合法性
                        if (Main.netMode != 2 || whoAmI == playerIndex || (Main.player[playerIndex].hostile && Main.player[whoAmI].hostile))
                        {
                            // 读取死亡原因
                            PlayerDeathReason deathReason = PlayerDeathReason.FromReader(reader);
                            // 读取伤害
                            int damage = reader.ReadInt16();
                            // 读取击退方向
                            int hitDirection = reader.ReadByte() - 1;
                            // 读取标志位
                            BitsByte flags = reader.ReadByte();
                            bool isCritical = flags[0];
                            bool isPvP = flags[1];
                            // 读取冷却计数
                            int cooldownCounter = reader.ReadSByte();
                            // 玩家受到伤害
                            Main.player[playerIndex].Hurt(deathReason, damage, hitDirection, isPvP, quiet: true, isCritical, cooldownCounter);
                            // 如果是服务器，转发消息

                            {
                                NetMessage.SendPlayerHurt(playerIndex, deathReason, damage, hitDirection, isCritical, isPvP, cooldownCounter, -1, whoAmI);
                            }
                        }

                        break;
                    }

                case 118:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取死亡原因
                        PlayerDeathReason deathReason = PlayerDeathReason.FromReader(reader);
                        // 读取伤害
                        int damage = reader.ReadInt16();
                        // 读取击退方向
                        int hitDirection = reader.ReadByte() - 1;
                        // 读取PvP标志
                        bool isPvP = ((BitsByte)reader.ReadByte())[0];
                        // 玩家死亡
                        Main.player[playerIndex].KillMe(deathReason, damage, hitDirection, isPvP);
                        // 如果是服务器，转发消息

                        {
                            NetMessage.SendPlayerDeath(playerIndex, deathReason, damage, hitDirection, isPvP, -1, whoAmI);
                        }

                        break;
                    }

                case 120:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取表情ID
                        int emoteId = reader.ReadByte();
                        if (emoteId >= 0 && emoteId < EmoteID.Count && Main.netMode == 2)
                        {
                            // 创建表情气泡
                            EmoteBubble.NewBubble(emoteId, new WorldUIAnchor(Main.player[playerIndex]), 360);
                            // 检查NPC反应
                            EmoteBubble.CheckForNPCsToReactToEmoteBubble(emoteId, Main.player[playerIndex]);
                        }

                        break;
                    }

                case 121:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取TileEntity ID
                        int tileEntityId = reader.ReadInt32();
                        // 读取操作索引
                        int actionIndex = reader.ReadByte();
                        // 读取参数
                        int param = reader.ReadByte();
                        // 尝试获取展示人偶
                        if (!TileEntity.TryGet<TEDisplayDoll>(tileEntityId, out var displayDoll))
                        {
                            TEDisplayDoll.ReadDummySync(actionIndex, param, reader);
                            break;
                        }

                        // 读取数据
                        displayDoll.ReadData(actionIndex, param, reader);
                        // 如果是服务器，转发消息

                        {
                            NetMessage.TrySendData(121, -1, playerIndex, null, playerIndex, tileEntityId, actionIndex, param);
                        }

                        break;
                    }

                case 122:
                    {
                        // 读取TileEntity ID
                        int tileEntityId = reader.ReadInt32();
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }


                        {
                            if (tileEntityId == -1)
                            {
                                // 清除交互锚点
                                Main.player[playerIndex].tileEntityAnchor.Clear();
                                NetMessage.TrySendData(122, -1, -1, null, tileEntityId, playerIndex);
                                break;
                            }

                            // 检查是否被占用并设置锚点
                            if (!TileEntity.IsOccupied(tileEntityId, out var _) && TileEntity.TryGet<TileEntity>(tileEntityId, out var tileEntity))
                            {
                                Main.player[playerIndex].tileEntityAnchor.Set(tileEntityId, tileEntity.Position.X, tileEntity.Position.Y);
                                NetMessage.TrySendData(122, -1, -1, null, tileEntityId, playerIndex);
                            }
                        }


                        break;
                    }

                case 123:
                    // 仅服务器处理

                    {
                        // 读取坐标
                        short x = reader.ReadInt16();
                        int y = reader.ReadInt16();
                        // 读取物品ID
                        int netId = reader.ReadInt16();
                        // 读取前缀
                        int prefix = reader.ReadByte();
                        // 读取堆叠数量
                        int stack = reader.ReadInt16();
                        // 尝试放置武器架
                        TEWeaponsRack.TryPlacing(x, y, netId, prefix, stack);
                    }

                    break;
                case 124:
                    {
                        // 读取玩家索引
                        int playerIndex = reader.ReadByte();

                        {
                            playerIndex = whoAmI;
                        }

                        // 读取TileEntity ID
                        int tileEntityId = reader.ReadInt32();
                        // 读取槽位索引
                        int slotIndex = reader.ReadByte();
                        bool isDye = false;
                        if (slotIndex >= 2)
                        {
                            isDye = true;
                            slotIndex -= 2;
                        }

                        // 尝试获取帽架
                        if (!TileEntity.TryGet<TEHatRack>(tileEntityId, out var hatRack) || slotIndex >= 2)
                        {
                            reader.ReadInt32();
                            reader.ReadByte();
                            break;
                        }

                        // 读取物品数据
                        hatRack.ReadItem(slotIndex, reader, isDye);
                        // 如果是服务器，转发消息

                        {
                            NetMessage.TrySendData(124, -1, playerIndex, null, playerIndex, tileEntityId, slotIndex, isDye.ToInt());
                        }

                        break;
                    }

                case 125:
                    {
                        int num195 = reader.ReadByte();
                        int num196 = reader.ReadInt16();
                        int num197 = reader.ReadInt16();
                        int num198 = reader.ReadByte();

                        {
                            num195 = whoAmI;
                        }




                        {
                            NetMessage.TrySendData(125, -1, num195, null, num195, num196, num197, num198);
                        }

                        break;
                    }

                case 126:


                    break;
                case 127:
                    {
                        int markerUniqueID = reader.ReadInt32();


                        break;
                    }

                case 128:
                    {
                        int num185 = reader.ReadByte();
                        int num186 = reader.ReadUInt16();
                        int num187 = reader.ReadUInt16();
                        int num188 = reader.ReadUInt16();
                        int num189 = reader.ReadUInt16();

                        NetMessage.SendData(128, -1, num185, null, num185, num188, num189, 0f, num186, num187);

                        break;
                    }

                case 129:

                    break;
                case 130:
                    {


                        int num166 = reader.ReadUInt16();
                        int num167 = reader.ReadUInt16();
                        int num168 = reader.ReadInt16();
                        if (num168 == 682)
                        {
                            if (NPC.unlockedSlimeRedSpawn)
                            {
                                break;
                            }

                            NPC.unlockedSlimeRedSpawn = true;
                            NetMessage.TrySendData(7);
                        }

                        num166 *= 16;
                        num167 *= 16;
                        NPC nPC4 = new NPC();
                        nPC4.SetDefaults(num168);
                        int type16 = nPC4.type;
                        int netID = nPC4.netID;
                        int num169 = NPC.NewNPC(new EntitySource_FishedOut(Main.player[whoAmI]), num166, num167, num168);
                        if (netID != type16)
                        {
                            Main.npc[num169].SetDefaults(netID);
                            NetMessage.TrySendData(23, -1, -1, null, num169);
                        }

                        if (num168 == 682)
                        {
                            WorldGen.CheckAchievement_RealEstateAndTownSlimes();
                        }

                        break;
                    }

                case 131:

                    break;
                case 132:

                    break;
                case 133:

                    {
                        short x9 = reader.ReadInt16();
                        int y9 = reader.ReadInt16();
                        int type13 = reader.ReadInt16();
                        int prefix3 = reader.ReadByte();
                        int stack6 = reader.ReadInt16();
                        TEFoodPlatter.TryPlacing(x9, y9, type13, prefix3, stack6);
                    }

                    break;
                case 134:
                    {
                        int num94 = reader.ReadByte();
                        int ladyBugLuckTimeLeft = reader.ReadInt32();
                        float torchLuck = reader.ReadSingle();
                        byte luckPotion = reader.ReadByte();
                        bool hasGardenGnomeNearby = reader.ReadBoolean();
                        bool brokenMirrorBadLuck = reader.ReadBoolean();
                        float equipmentBasedLuckBonus = reader.ReadSingle();
                        float coinLuck = reader.ReadSingle();
                        byte kiteLuckLevel = reader.ReadByte();

                        {
                            num94 = whoAmI;
                        }

                        Player obj3 = Main.player[num94];
                        obj3.ladyBugLuckTimeLeft = ladyBugLuckTimeLeft;
                        obj3.torchLuck = torchLuck;
                        obj3.luckPotion = luckPotion;
                        obj3.HasGardenGnomeNearby = hasGardenGnomeNearby;
                        obj3.brokenMirrorBadLuck = brokenMirrorBadLuck;
                        obj3.equipmentBasedLuckBonus = equipmentBasedLuckBonus;
                        obj3.coinLuck = coinLuck;
                        obj3.kiteLuckLevel = kiteLuckLevel;
                        obj3.RecalculateLuck();

                        {
                            NetMessage.SendData(134, -1, num94, null, num94);
                        }

                        break;
                    }

                case 135:
                    {
                        int num93 = reader.ReadByte();


                        break;
                    }

                case 136:
                    {
                        for (int n = 0; n < 2; n++)
                        {
                            for (int num90 = 0; num90 < 3; num90++)
                            {
                                NPC.cavernMonsterType[n, num90] = reader.ReadUInt16();
                            }
                        }

                        break;
                    }

                case 137:

                    {
                        int num85 = reader.ReadInt16();
                        int buffTypeToRemove = reader.ReadUInt16();
                        if (num85 >= 0 && num85 < Main.maxNPCs)
                        {
                            Main.npc[num85].RequestBuffRemoval(buffTypeToRemove);
                        }
                    }

                    break;
                case 139:

                    break;
                case 140:
                    {
                        int num82 = reader.ReadByte();
                        int num83 = reader.ReadInt32();
                        switch (num82)
                        {
                            case 0:


                                break;
                            case 1:

                                {
                                    NPC.TransformCopperSlime(num83);
                                }

                                break;
                            case 2:

                                {
                                    NPC.TransformElderSlime(num83);
                                }

                                break;
                        }

                        break;
                    }

                case 141:
                    {
                        LucyAxeMessage.MessageSource messageSource = (LucyAxeMessage.MessageSource)reader.ReadByte();
                        byte b7 = reader.ReadByte();
                        Vector2 velocity = reader.ReadVector2();
                        int num78 = reader.ReadInt32();
                        int num79 = reader.ReadInt32();


                        NetMessage.SendData(141, -1, whoAmI, null, (int)messageSource, (int)b7, velocity.X, velocity.Y, num78, num79);
                        // else
                        // {
                        //     LucyAxeMessage.CreateFromNet(messageSource, b7, new Vector2(num78, num79), velocity);
                        // }

                        break;
                    }

                case 142:
                    {
                        int num75 = reader.ReadByte();

                        {
                            num75 = whoAmI;
                        }

                        Player obj = Main.player[num75];
                        obj.piggyBankProjTracker.TryReading(reader);
                        obj.voidLensChest.TryReading(reader);

                        {
                            NetMessage.TrySendData(142, -1, whoAmI, null, num75);
                        }

                        break;
                    }

                case 143:

                    {
                        DD2Event.AttemptToSkipWaitTime();
                    }

                    break;
                case 144:

                    {
                        NPC.HaveDryadDoStardewAnimation();
                    }

                    break;
                case 146:
                    switch (reader.ReadByte())
                    {
                        case 0:
                            WorldItem.ShimmerEffect(reader.ReadVector2());
                            break;
                        case 1:
                            {
                                Vector2 coinPosition = reader.ReadVector2();
                                int coinAmount = reader.ReadInt32();
                                Main.player[Main.myPlayer].AddCoinLuck(coinPosition, coinAmount);
                                break;
                            }
                    }

                    break;
                case 147:
                    {
                        int num68 = reader.ReadByte();

                        {
                            num68 = whoAmI;
                        }

                        int num69 = reader.ReadByte();
                        Main.player[num68].TrySwitchingLoadout(num69);
                        ReadAccessoryVisibility(reader, Main.player[num68].hideVisibleAccessory);

                        {
                            NetMessage.TrySendData(b, -1, num68, null, num68, num69);
                        }

                        break;
                    }

                case 149:

                    {
                        short x4 = reader.ReadInt16();
                        int y4 = reader.ReadInt16();
                        int type10 = reader.ReadInt16();
                        int prefix2 = reader.ReadByte();
                        int stack4 = reader.ReadInt16();
                        TEDeadCellsDisplayJar.TryPlacing(x4, y4, type10, prefix2, stack4);
                    }

                    break;
                case 150:
                    {
                        int num50 = reader.ReadByte();

                        {
                            num50 = whoAmI;
                        }

                        int num51 = reader.ReadInt16();
                        Player player5 = Main.player[num50];


                        if (num51 >= 0)
                        {
                            player5.SetOrRequestSpectating(num51);
                            break;
                        }

                        player5.spectating = -1;
                        NetMessage.SendData(150, -1, whoAmI, null, whoAmI, num51);

                        // else if (player5 != Main.LocalPlayer || player5.spectating >= 0)
                        // {
                        //     player5.spectating = num51;
                        // }

                        break;
                    }

                case 152:
                    {
                        int num39 = reader.ReadByte();


                        num39 = whoAmI;




                        NetMessage.TrySendData(152, -1, whoAmI, null, num39);




                        break;
                    }

                case 153:
                    {
                        int num37 = reader.ReadByte();
                        int num38 = reader.ReadInt16();
                        Main.npc[num37].GetHurtByDebuff(num38);


                        NetMessage.TrySendData(153, -1, whoAmI, null, num37, num38);


                        break;
                    }

                case 154:


                    NetMessage.TrySendData(154, whoAmI);

                    // else
                    // {
                    //     Ping.PingRecieved();
                    // }

                    break;
                case 155:
                    {
                        short num32 = reader.ReadInt16();
                        short newSize = reader.ReadInt16();
                        if (num32 >= 0 && num32 < 8000)
                        {
                            Main.chest[num32].Resize(newSize);
                        }

                        break;
                    }

                case 156:
                    Point16 point = new Point16(reader.ReadInt16(), reader.ReadInt16());
                    int itemType = reader.ReadInt16();
                    if (TileEntity.TryGetAt<TELeashedEntityAnchorWithItem>(point.X, point.Y, out var result))
                    {
                        result.InsertItem(itemType);
                    }

                    break;
                case 158:


                    break;
                case 159:

                    int sectionX = reader.ReadUInt16();
                    int sectionY = reader.ReadUInt16();
                    NetMessage.SendSection(whoAmI, sectionX, sectionY);

                    break;
                case 160:


                    break;
                case 161:
                    {
                        string text = reader.ReadString();
                        Main.player[whoAmI].host = !string.IsNullOrWhiteSpace(Netplay.HostToken) && Netplay.HostToken == text;
                        break;
                    }

                default:
                    if (Main.netMode == 2 && Netplay.Clients[whoAmI].State == 0)
                    {
                        NetMessage.BootPlayer(whoAmI, Lang.mp[2].ToNetworkText());
                    }

                    break;
                case 15:
                case 25:
                case 26:
                case 44:
                case 67:
                case 83:
                case 93:
                    break;
            }
        }
    }

    private static void ReadAccessoryVisibility(BinaryReader reader, bool[] hideVisibleAccessory)
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.ReadAccessoryVisibility"))
        {
            ushort num = reader.ReadUInt16();
            for (int i = 0; i < hideVisibleAccessory.Length; i++)
            {
                hideVisibleAccessory[i] = (num & (1 << i)) != 0;
            }
        }
    }

    private static void TrySendingItemArray(int plr, Item[] array, int slotStartIndex)
    {
        using (new global::Terraria.CallTracker("Terraria.MessageBuffer.TrySendingItemArray"))
        {
            for (int i = 0; i < array.Length; i++)
            {
                NetMessage.TrySendData(5, -1, -1, null, plr, slotStartIndex + i);
            }
        }
    }
}