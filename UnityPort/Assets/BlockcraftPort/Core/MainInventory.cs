using System;
using UnityEngine;

namespace BlockcraftPort
{
    [Serializable]
    public sealed class MainInventoryStackData
    {
        public bool block;
        public int blockId;
        public string itemKey;
        public int count;
        // -1 is source JS "dur === undefined". Durability is initialized lazily by dn().
        public int dur=-1;

        public MainInventoryStackData Clone()
        {
            return new MainInventoryStackData{block=block,blockId=blockId,itemKey=itemKey,count=count,dur=dur};
        }
    }

    public enum MainToolKind : byte { None, Pickaxe, Axe, Shovel, Sword, Hoe, Shears, Bow, Rod }

    public readonly struct MainToolDef
    {
        public readonly MainToolKind Kind;
        public readonly int Tier;
        public readonly float Speed;
        public readonly int Durability;
        public readonly float Damage;
        public readonly float AttackSpeed;
        public MainToolDef(MainToolKind kind,int tier,float speed,int durability,float damage=1f,float attackSpeed=1f)
        {Kind=kind;Tier=tier;Speed=speed;Durability=durability;Damage=damage;AttackSpeed=attackSpeed;}
        public bool Valid=>Kind!=MainToolKind.None;
    }

    /// <summary>
    /// Source main.js M1/Wr()/Pt() subset used by the gameplay already present in this Unity port.
    /// The standard five tool materials exactly mirror main Fl/zI: tier [1,2,3,1,4],
    /// speed [2,4,6,12,8], durability [59,131,250,32,1561].
    /// </summary>
    public static class MainInventoryCatalog
    {
        static readonly string[] ToolMaterials={"wooden","stone","iron","golden","diamond"};
        static readonly int[] ToolTiers={1,2,3,1,4};
        static readonly float[] ToolSpeeds={2f,4f,6f,12f,8f};
        static readonly int[] ToolDurability={59,131,250,32,1561};
        static readonly float[] SwordDamage={4f,5f,6f,4f,7f};
        static readonly float[] AxeDamage={7f,9f,9f,7f,9f};
        static readonly float[] PickDamage={2f,3f,4f,2f,5f};
        static readonly float[] ShovelDamage={2.5f,3.5f,4.5f,2.5f,5.5f};
        static readonly float[] SwordAttack={1.6f,1.6f,1.6f,1.6f,1.6f};
        static readonly float[] AxeAttack={.8f,.8f,.9f,1f,1f};
        static readonly float[] PickAttack={1.2f,1.2f,1.2f,1.2f,1.2f};

        public static int MaxStack(MainInventoryStackData s)
        {
            if(s==null)return 64;
            if(s.block)return 64;
            return MaxStack(s.itemKey);
        }

        public static int MaxStack(string key)
        {
            if(string.IsNullOrEmpty(key))return 64;
            if(TryGetTool(key,out _))return 1;
            switch(key)
            {
                case "item_ender_pearl": case "item_egg": case "item_bucket": return 16;
                case "item_boat": case "item_minecart": case "item_water_bucket": case "item_lava_bucket":
                case "item_mushroom_stew": case "flint_and_steel": case "item_flint_and_steel": return 1;
                default:
                    // Source beds and armour are non-stackable. Keep this rule textual so every atlas colour works.
                    if(key.StartsWith("item_bed_",StringComparison.Ordinal)||key.EndsWith("_helmet",StringComparison.Ordinal)||
                       key.EndsWith("_chestplate",StringComparison.Ordinal)||key.EndsWith("_leggings",StringComparison.Ordinal)||
                       key.EndsWith("_boots",StringComparison.Ordinal))return 1;
                    return 64;
            }
        }

        public static bool TryGetTool(string key,out MainToolDef tool)
        {
            tool=default(MainToolDef);
            if(string.IsNullOrEmpty(key))return false;
            if(key=="item_shears") {tool=new MainToolDef(MainToolKind.Shears,1,1f,238,1f,1f);return true;}
            if(key=="item_bow") {tool=new MainToolDef(MainToolKind.Bow,0,1f,384,1f,1f);return true;}
            if(key=="item_fishing_rod"||key=="item_fishing_rod_cast") {tool=new MainToolDef(MainToolKind.Rod,1,1f,64,1f,1f);return true;}

            for(int m=0;m<ToolMaterials.Length;m++)
            {
                string p="item_"+ToolMaterials[m]+"_";
                if(!key.StartsWith(p,StringComparison.Ordinal))continue;
                string k=key.Substring(p.Length);
                switch(k)
                {
                    case "pickaxe": tool=new MainToolDef(MainToolKind.Pickaxe,ToolTiers[m],ToolSpeeds[m],ToolDurability[m],PickDamage[m],PickAttack[m]);return true;
                    case "axe": tool=new MainToolDef(MainToolKind.Axe,ToolTiers[m],ToolSpeeds[m],ToolDurability[m],AxeDamage[m],AxeAttack[m]);return true;
                    case "shovel": tool=new MainToolDef(MainToolKind.Shovel,ToolTiers[m],ToolSpeeds[m],ToolDurability[m],ShovelDamage[m],1f);return true;
                    case "sword": tool=new MainToolDef(MainToolKind.Sword,ToolTiers[m],ToolSpeeds[m],ToolDurability[m],SwordDamage[m],SwordAttack[m]);return true;
                    case "hoe": tool=new MainToolDef(MainToolKind.Hoe,ToolTiers[m],ToolSpeeds[m],ToolDurability[m],1f,1f);return true;
                }
            }
            return false;
        }

        public static int FoodPoints(string key)
        {
            switch(key)
            {
                case "item_apple": return 4;
                case "item_sweet_berries": return 2;
                case "item_melon_slice": return 2;
                case "item_porkchop": return 3;
                case "item_cooked_porkchop": return 8;
                case "item_beef": return 3;
                case "item_cooked_beef": return 8;
                case "item_mutton": return 2;
                case "item_cooked_mutton": return 6;
                case "item_chicken": return 2;
                case "item_cooked_chicken": return 6;
                case "item_rotten_flesh": return 2;
                case "item_bread": return 5;
                case "item_carrot": return 3;
                case "item_potato": return 1;
                case "item_baked_potato": return 5;
                case "item_golden_apple": return 9;
                case "item_golden_carrot": return 6;
                case "item_mushroom_stew": return 6;
                case "item_raw_cod": return 2;
                case "item_cooked_cod": return 5;
                case "item_raw_salmon": return 2;
                case "item_cooked_salmon": return 6;
                case "item_spider_eye": return 2;
                case "item_pumpkin_pie": return 8;
                default:return 0;
            }
        }

        public static int DefaultDurability(string key)
        {
            if(string.IsNullOrEmpty(key))return 0;
            if(key=="flint_and_steel"||key=="item_flint_and_steel")return 64; // main ai
            return TryGetTool(key,out MainToolDef t)?t.Durability:0;
        }

        public static string MobKey(MobItemId id)
        {
            string tex=MobItemCatalog.TextureKey(id);
            return string.IsNullOrEmpty(tex)?"mob:"+((int)id).ToString():tex;
        }

        public static bool TryMobId(string key,out MobItemId id)
        {
            id=MobItemCatalog.FromTextureKey(key);
            if(id!=MobItemId.None)return true;
            if(!string.IsNullOrEmpty(key)&&key.StartsWith("mob:",StringComparison.Ordinal)&&int.TryParse(key.Substring(4),out int n)&&Enum.IsDefined(typeof(MobItemId),n))
            {id=(MobItemId)n;return id!=MobItemId.None;}
            return false;
        }

        public static bool TryVisibleItemKey(MainInventoryStackData s,out string key)
        {
            key=null;if(s==null||s.block||string.IsNullOrEmpty(s.itemKey))return false;
            if(s.itemKey.StartsWith("mob:",StringComparison.Ordinal))return false;
            key=s.itemKey;return true;
        }
    }

    /// <summary>
    /// Exact behavioral port of main.js hn()/Dt()/Jt()/_r()/dn()/l7() for the 36 survival slots.
    /// UI/crafting are separate systems; this class is the authoritative stack and durability state.
    /// </summary>
    public sealed class MainSurvivalInventory
    {
        public const int SlotCount=36;
        public const int HotbarCount=9;
        readonly MainInventoryStackData[] slots=new MainInventoryStackData[SlotCount];
        int hotbarSel;

        public int HotbarSelected=>hotbarSel;
        public MainInventoryStackData Selected=>slots[hotbarSel];
        public MainInventoryStackData Get(int slot)=>slot>=0&&slot<SlotCount?slots[slot]:null;
        public bool Set(int slot,MainInventoryStackData value)
        {
            if(slot<0||slot>=SlotCount)return false;
            if(value!=null)
            {
                value=value.Clone();
                int max=MainInventoryCatalog.MaxStack(value);
                value.count=Mathf.Clamp(value.count,1,max);
            }
            slots[slot]=value;return true;
        }
        public MainInventoryStackData Take(int slot)
        {
            if(slot<0||slot>=SlotCount)return null;var s=slots[slot];slots[slot]=null;return s;
        }

        public void SelectHotbar(int index){hotbarSel=Mathf.Clamp(index,0,HotbarCount-1);}
        public void StepHotbar(int delta)
        {
            int n=(hotbarSel+delta)%HotbarCount;if(n<0)n+=HotbarCount;hotbarSel=n;
        }

        public int AddBlock(BlockId id,int count)
        {
            if(id==BlockId.Air||count<=0)return count;
            return Add(new MainInventoryStackData{block=true,blockId=(int)id,count=count,dur=-1});
        }
        public int AddItem(string key,int count,int dur=-1)
        {
            if(string.IsNullOrEmpty(key)||count<=0)return count;
            return Add(new MainInventoryStackData{block=false,itemKey=key,count=count,dur=dur});
        }

        // main.js Jt(): merge only into a same-id stack whose durability is undefined, then use empty slots.
        public int Add(MainInventoryStackData incoming)
        {
            if(incoming==null||incoming.count<=0)return 0;
            int max=MainInventoryCatalog.MaxStack(incoming),left=incoming.count;
            if(max>1)
            {
                for(int i=0;i<SlotCount&&left>0;i++)
                {
                    MainInventoryStackData s=slots[i];
                    if(s==null||s.dur>=0||!SameIdentity(s,incoming)||s.count>=max)continue;
                    int n=Math.Min(max-s.count,left);s.count+=n;left-=n;
                }
            }
            for(int i=0;i<SlotCount&&left>0;i++)if(slots[i]==null)
            {
                int n=Math.Min(max,left);var s=incoming.Clone();s.count=n;slots[i]=s;left-=n;
            }
            return left;
        }

        public static bool SameIdentity(MainInventoryStackData a,MainInventoryStackData b)
        {
            if(a.block!=b.block)return false;
            return a.block?a.blockId==b.blockId:string.Equals(a.itemKey,b.itemKey,StringComparison.Ordinal);
        }

        // main.js _r(). Creative bypass lives in MainPlayerController, as in source Je().
        public bool ConsumeSelected(int count=1)
        {
            count=Math.Max(1,count);var s=Selected;if(s==null||s.count<count)return false;
            s.count-=count;if(s.count<=0)slots[hotbarSel]=null;return true;
        }

        // main.js dn(): lazy durability initialization, then remove a broken stack.
        public bool DamageSelected(int amount,out bool broke)
        {
            broke=false;amount=Math.Max(1,amount);var s=Selected;if(s==null||s.block)return false;
            if(s.dur<0)
            {
                int durability=MainInventoryCatalog.DefaultDurability(s.itemKey);if(durability<=0)return false;
                s.dur=durability;
            }
            s.dur-=amount;if(s.dur<=0){slots[hotbarSel]=null;broke=true;}return true;
        }

        // main.js ST(): bucket/stew container replacement. If the selected stack has one item,
        // replace it in-place; otherwise consume one and insert the replacement as a normal stack.
        public int ReplaceSelectedWithItem(string key)
        {
            if(string.IsNullOrEmpty(key))return 1;var s=Selected;if(s==null)return 1;
            if(s.count==1){slots[hotbarSel]=new MainInventoryStackData{block=false,itemKey=key,count=1,dur=-1};return 0;}
            s.count--;return AddItem(key,1,-1);
        }

        public MainInventoryStackData[] Capture()
        {
            var r=new MainInventoryStackData[SlotCount];for(int i=0;i<SlotCount;i++)if(slots[i]!=null)r[i]=slots[i].Clone();return r;
        }
        public void Restore(MainInventoryStackData[] data,int selected)
        {
            Array.Clear(slots,0,slots.Length);hotbarSel=Mathf.Clamp(selected,0,HotbarCount-1);
            if(data==null)return;int n=Math.Min(SlotCount,data.Length);
            for(int i=0;i<n;i++)if(data[i]!=null&&data[i].count>0)
            {
                var s=data[i].Clone();if(s.block&&(s.blockId<=0||s.blockId>ushort.MaxValue))continue;
                int max=MainInventoryCatalog.MaxStack(s);s.count=Mathf.Clamp(s.count,1,max);slots[i]=s;
            }
        }
        public MainInventoryStackData[] ClearAndCapture()
        {
            var r=Capture();Array.Clear(slots,0,slots.Length);return r;
        }
    }
}
