using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockcraftPort
{
    [Serializable]
    public sealed class MainFurnaceSaveData
    {
        // Source-space world position. Z is reflected only at the save boundary.
        public int x,y,z;
        public MainInventoryStackData inp,fuel,outp;
        public float prog,burn,burnMax;
    }

    [Serializable]
    public sealed class MainChestSaveData
    {
        public int x,y,z;
        public MainInventoryStackData[] slots=Array.Empty<MainInventoryStackData>();
    }

    /// <summary>
    /// R52 static-world functional block port. This intentionally has no moving-ship integration.
    /// Source anchors: X8/fs/PD/W8/Z8/m4 (furnace), ws/ls/q8/j8/Qs/$8/_4 (chests),
    /// PA/SE/Ld (recipes) and HE/NR/f3/w3 (2x2/3x3 crafting + screens).
    /// </summary>
    public sealed class MainWorldFunctionalBlocks : MonoBehaviour
    {
        sealed class FurnaceState
        {
            public MainInventoryStackData inp,fuel,outp;
            public float prog,burn,burnMax;
            public bool lit;
        }

        enum ScreenKind : byte { None, Inventory2, Crafting3, Furnace, Chest }

        static MainWorldFunctionalBlocks instance;
        public static MainWorldFunctionalBlocks Instance=>instance;

        VoxelWorld world;
        MainPlayerController player;
        readonly Dictionary<Vector3Int,FurnaceState> furnaces=new Dictionary<Vector3Int,FurnaceState>();
        readonly Dictionary<Vector3Int,MainInventoryStackData[]> chests=new Dictionary<Vector3Int,MainInventoryStackData[]>();
        ScreenKind screen;
        Vector3Int openedBlock;
        readonly MainInventoryStackData[] craftGrid=new MainInventoryStackData[9];
        int craftSize=2;
        MainInventoryStackData cursor;
        MainRecipe matchedRecipe;
        Texture2D itemAtlas;
        Texture2D blockAtlas;
        GUIStyle tinyStyle,titleStyle;

        public bool UiOpen=>screen!=ScreenKind.None;

        public static MainWorldFunctionalBlocks EnsureInstance(VoxelWorld w,MainPlayerController p,MainFurnaceSaveData[] savedFurnaces=null,MainChestSaveData[] savedChests=null)
        {
            if(instance==null)
            {
                var go=new GameObject("MainWorldFunctionalBlocks");
                instance=go.AddComponent<MainWorldFunctionalBlocks>();
            }
            instance.Configure(w,p,savedFurnaces,savedChests);
            return instance;
        }

        void Awake(){instance=this;}
        void OnDestroy(){if(instance==this)instance=null;}

        public void Configure(VoxelWorld w,MainPlayerController p,MainFurnaceSaveData[] savedFurnaces,MainChestSaveData[] savedChests)
        {
            world=w;player=p;furnaces.Clear();chests.Clear();CloseUi(false);
            RestoreFurnaces(savedFurnaces);RestoreChests(savedChests);
            itemAtlas=Resources.Load<Texture2D>("Voxel/main_items");
            blockAtlas=Resources.Load<Texture2D>("Voxel/atlas");
        }

        void Update()
        {
            if(world==null||player==null)return;
            TickFurnaces(Mathf.Min(Time.unscaledDeltaTime,.1f));
            if(Input.GetKeyDown(KeyCode.E))
            {
                if(UiOpen)CloseUi(true);else if(!player.IsCreative)OpenInventory(false);
            }
            if(UiOpen&&Input.GetKeyDown(KeyCode.Escape))CloseUi(true);
        }

        public static bool TryInteractWorld(VoxelHit hit)
        {
            if(instance==null||instance.world==null)return false;
            switch(hit.Id)
            {
                case BlockId.CraftingTable: instance.OpenInventory(true);return true;
                case BlockId.Furnace:
                case BlockId.FurnaceLit: instance.OpenFurnace(hit.Block);return true;
                case BlockId.Chest: instance.OpenChest(hit.Block);return true;
                default:return false;
            }
        }

        public static bool PlaceChestWorld(Vector3Int pos,float mainYawRadians,bool playerEdit=true)
        {
            if(instance==null||instance.world==null)return false;
            // main Ao(): source dir from yaw, then placement metadata is (Ao()+2)%4.
            float sx=Mathf.Sin(mainYawRadians),sz=-Mathf.Cos(mainYawRadians);
            int ao=Mathf.Abs(sx)>Mathf.Abs(sz)?(sx>0f?3:1):(sz>0f?0:2);
            byte sourceMeta=(byte)((ao+2)&3);
            byte meta=SourceCoords.SourceMetaToUnity(BlockId.Chest,sourceMeta);
            if(!instance.world.SetBlock(pos.x,pos.y,pos.z,BlockId.Chest,meta,playerEdit))return false;
            instance.TryPairChest(pos,meta&3);
            instance.MarkDirty();return true;
        }

        static byte PairMeta(int unityDir,int dxUnity,int dzUnity)
        {
            // source ls(dir,dx,dz), evaluated in source coordinates and mirrored back once.
            int sourceDir=SourceCoords.MirrorFacing(unityDir&3);
            int dxSource=dxUnity,dzSource=-dzUnity;
            int fx=sourceDir==1?-1:sourceDir==3?1:0;
            int fz=sourceDir==0?1:sourceDir==2?-1:0;
            bool right=dxSource==fz&&dzSource==-fx;
            byte sourceMeta=(byte)(sourceDir|4|(right?8:0));
            return SourceCoords.SourceMetaToUnity(BlockId.Chest,sourceMeta);
        }
        void TryPairChest(Vector3Int p,int dir)
        {
            Vector3Int[] side=(dir==0||dir==2)?new[]{Vector3Int.right,Vector3Int.left}:new[]{new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
            for(int i=0;i<side.Length;i++)
            {
                Vector3Int q=p+side[i];
                if(world.GetBlock(q.x,q.y,q.z)!=BlockId.Chest)continue;
                byte qm=world.GetMeta(q.x,q.y,q.z);
                if((qm&3)!=dir||(qm&4)!=0)continue;
                world.SetBlock(p.x,p.y,p.z,BlockId.Chest,PairMeta(dir,q.x-p.x,q.z-p.z),true);
                world.SetBlock(q.x,q.y,q.z,BlockId.Chest,PairMeta(dir,p.x-q.x,p.z-q.z),true);
                break;
            }
        }

        public static void NotifyWorldBlockReplacing(int x,int y,int z,BlockId oldId,byte oldMeta,BlockId newId,byte newMeta)
        {
            if(instance==null)return;
            if((oldId==BlockId.Furnace||oldId==BlockId.FurnaceLit)&&(newId!=BlockId.Furnace&&newId!=BlockId.FurnaceLit))
                instance.RemoveFurnace(new Vector3Int(x,y,z),true);
            if(oldId==BlockId.Chest&&newId!=BlockId.Chest)
                instance.RemoveChest(new Vector3Int(x,y,z),oldMeta,true);
        }

        void RemoveFurnace(Vector3Int p,bool drop)
        {
            if(!furnaces.TryGetValue(p,out FurnaceState f))return;
            furnaces.Remove(p);
            if(drop){DropStack(p,f.inp);DropStack(p,f.fuel);DropStack(p,f.outp);}MarkDirty();
            if(screen==ScreenKind.Furnace&&openedBlock==p)CloseUi(true);
        }
        void RemoveChest(Vector3Int p,byte oldMeta,bool drop)
        {
            MainInventoryStackData[] s=GetChest(p,false);chests.Remove(p);
            if(drop&&s!=null)for(int i=0;i<s.Length;i++)DropStack(p,s[i]);
            if((oldMeta&4)!=0)
            {
                int dir=oldMeta&3;Vector3Int[] side=(dir==0||dir==2)?new[]{Vector3Int.right,Vector3Int.left}:new[]{new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
                for(int i=0;i<side.Length;i++)
                {
                    Vector3Int q=p+side[i];if(world.GetBlock(q.x,q.y,q.z)!=BlockId.Chest)continue;
                    byte qm=world.GetMeta(q.x,q.y,q.z);if((qm&3)==dir&&(qm&4)!=0){world.SetBlock(q.x,q.y,q.z,BlockId.Chest,(byte)dir,true);break;}
                }
            }
            MarkDirty();if(screen==ScreenKind.Chest)CloseUi(true);
        }
        void DropStack(Vector3Int p,MainInventoryStackData s)
        {
            if(s==null||s.count<=0)return;
            float x=p.x+.5f,y=p.y+.5f,z=p.z+.5f;
            if(s.block)MainTransientRenderer.SpawnDroppedBlock(x,y,z,(BlockId)s.blockId,s.count);
            else MainTransientRenderer.SpawnDroppedItemKey(x,y,z,s.itemKey,s.count,s.dur);
        }

        FurnaceState GetFurnace(Vector3Int p,bool create=true)
        {
            if(furnaces.TryGetValue(p,out FurnaceState f))return f;
            if(!create)return null;f=new FurnaceState();furnaces[p]=f;return f;
        }
        MainInventoryStackData[] GetChest(Vector3Int p,bool create=true)
        {
            if(chests.TryGetValue(p,out MainInventoryStackData[] s))return s;
            if(!create)return null;s=new MainInventoryStackData[27];chests[p]=s;return s;
        }

        void TickFurnaces(float dt)
        {
            if(dt<=0f||furnaces.Count==0)return;
            foreach(var kv in furnaces)
            {
                Vector3Int p=kv.Key;FurnaceState f=kv.Value;
                if(world.GetBlock(p.x,p.y,p.z)!=BlockId.Furnace&&world.GetBlock(p.x,p.y,p.z)!=BlockId.FurnaceLit)continue;
                if(f.burn>0f)f.burn=Mathf.Max(0f,f.burn-dt);
                bool lit=f.burn>0f;
                if(lit!=f.lit){f.lit=lit;BlockId id=world.GetBlock(p.x,p.y,p.z);if(lit&&id==BlockId.Furnace)world.SetBlock(p.x,p.y,p.z,BlockId.FurnaceLit,world.GetMeta(p.x,p.y,p.z),false);else if(!lit&&id==BlockId.FurnaceLit)world.SetBlock(p.x,p.y,p.z,BlockId.Furnace,world.GetMeta(p.x,p.y,p.z),false);}
                MainInventoryStackData smelt=MainFurnaceCatalog.SmeltResult(f.inp);
                bool can=smelt!=null&&CanMerge(f.outp,smelt);
                if(!can){f.prog=Mathf.Max(0f,f.prog-dt*.3f);continue;}
                if(f.burn<=0f)
                {
                    float fuel=MainFurnaceCatalog.FuelSeconds(f.fuel);
                    if(fuel>0f)
                    {
                        f.burnMax=f.burn=fuel;bool lava=f.fuel!=null&&!f.fuel.block&&f.fuel.itemKey=="item_lava_bucket";
                        ConsumeOne(ref f.fuel);if(lava&&f.fuel==null)f.fuel=new MainInventoryStackData{itemKey="item_bucket",count=1,dur=-1};MarkDirty();
                    }
                    else{f.prog=Mathf.Max(0f,f.prog-dt*.3f);continue;}
                }
                f.prog+=dt/10f; // main wD=10
                if(f.prog>=1f)
                {
                    f.prog=0f;MergeOne(ref f.outp,smelt);ConsumeOne(ref f.inp);MarkDirty();
                }
            }
        }
        static bool CanMerge(MainInventoryStackData dst,MainInventoryStackData src)
        {return dst==null||(MainSurvivalInventory.SameIdentity(dst,src)&&dst.dur<0&&src.dur<0&&dst.count<MainInventoryCatalog.MaxStack(dst));}
        static void MergeOne(ref MainInventoryStackData dst,MainInventoryStackData src)
        {if(dst==null){dst=src.Clone();dst.count=1;}else dst.count++;}
        static void ConsumeOne(ref MainInventoryStackData s){if(s==null)return;if(--s.count<=0)s=null;}
        void MarkDirty(){world?.MarkPersistentSaveDirty();}

        void OpenInventory(bool table)
        {
            if(player==null)return;CloseUi(false);screen=table?ScreenKind.Crafting3:ScreenKind.Inventory2;craftSize=table?3:2;Array.Clear(craftGrid,0,craftGrid.Length);matchedRecipe=null;BeginUi();
        }
        void OpenFurnace(Vector3Int p){CloseUi(false);openedBlock=p;GetFurnace(p,true);screen=ScreenKind.Furnace;BeginUi();}
        void OpenChest(Vector3Int p){CloseUi(false);openedBlock=p;GetChest(p,true);screen=ScreenKind.Chest;BeginUi();}
        void BeginUi(){player.SetGameplayUiOpen(true);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        void CloseUi(bool returnCraft)
        {
            if(returnCraft&&(screen==ScreenKind.Inventory2||screen==ScreenKind.Crafting3))ReturnCraftingGrid();
            if(returnCraft&&cursor!=null)ReturnOrDrop(cursor);
            cursor=null;matchedRecipe=null;screen=ScreenKind.None;
            if(player!=null){player.SetGameplayUiOpen(false);Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        }
        void ReturnCraftingGrid()
        {
            int n=craftSize*craftSize;for(int i=0;i<n;i++)if(craftGrid[i]!=null){ReturnOrDrop(craftGrid[i]);craftGrid[i]=null;}
        }
        void ReturnOrDrop(MainInventoryStackData s)
        {
            if(s==null)return;int left=player.SurvivalInventory.Add(s.Clone());if(left>0){var d=s.Clone();d.count=left;Vector3 p=player.transform.position;if(d.block)MainTransientRenderer.SpawnDroppedBlock(p.x,p.y+.6f,p.z,(BlockId)d.blockId,d.count);else MainTransientRenderer.SpawnDroppedItemKey(p.x,p.y+.6f,p.z,d.itemKey,d.count,d.dur);}MarkDirty();
        }

        List<Vector3Int> ChestSections(Vector3Int p)
        {
            var list=new List<Vector3Int>(2){p};byte m=world.GetMeta(p.x,p.y,p.z);if((m&4)==0)return list;int dir=m&3;
            Vector3Int[] side=(dir==0||dir==2)?new[]{Vector3Int.left,Vector3Int.right}:new[]{new Vector3Int(0,0,-1),new Vector3Int(0,0,1)};
            for(int i=0;i<side.Length;i++){Vector3Int q=p+side[i];if(world.GetBlock(q.x,q.y,q.z)!=BlockId.Chest)continue;byte qm=world.GetMeta(q.x,q.y,q.z);if((qm&3)==dir&&(qm&4)!=0){list.Add(q);break;}}
            if(list.Count==2&&(list[1].x<list[0].x||(list[1].x==list[0].x&&list[1].z<list[0].z))){Vector3Int q=list[0];list[0]=list[1];list[1]=q;}return list;
        }

        void EnsureStyles()
        {
            if(tinyStyle!=null)return;tinyStyle=new GUIStyle(GUI.skin.button){fontSize=10,wordWrap=true,alignment=TextAnchor.MiddleCenter};titleStyle=new GUIStyle(GUI.skin.label){fontSize=22,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        }
        void OnGUI()
        {
            if(!UiOpen||player==null)return;EnsureStyles();float sw=Screen.width,sh=Screen.height;GUI.Box(new Rect(0,0,sw,sh),GUIContent.none);
            float w=Mathf.Min(780,sw-30),h=Mathf.Min(620,sh-30),x=(sw-w)*.5f,y=(sh-h)*.5f;GUI.Box(new Rect(x,y,w,h),GUIContent.none);
            string title=screen==ScreenKind.Inventory2?"Inventory / Инвентарь":screen==ScreenKind.Crafting3?"Crafting / Верстак":screen==ScreenKind.Furnace?"Furnace / Печь":screen==ScreenKind.Chest?(ChestSections(openedBlock).Count>1?"Large Chest / Большой сундук":"Chest / Сундук"):"";
            GUI.Label(new Rect(x+20,y+10,w-40,34),title,titleStyle);
            if(GUI.Button(new Rect(x+w-42,y+10,30,30),"×"))CloseUi(true);
            if(screen==ScreenKind.Inventory2||screen==ScreenKind.Crafting3)DrawCrafting(x,y,w,h);
            else if(screen==ScreenKind.Furnace)DrawFurnace(x,y,w,h);
            else if(screen==ScreenKind.Chest)DrawChest(x,y,w,h);
            DrawPlayerInventory(x+22,y+h-230,w-44);
            if(cursor!=null)DrawStack(new Rect(Event.current.mousePosition.x-26,Event.current.mousePosition.y-26,52,52),cursor,false);
        }

        void DrawCrafting(float x,float y,float w,float h)
        {
            float cell=56,gx=x+70,gy=y+70;int n=craftSize*craftSize;for(int i=0;i<n;i++){int k=i;DrawInteractiveSlot(new Rect(gx+(i%craftSize)*cell,gy+(i/craftSize)*cell,52,52),()=>craftGrid[k],v=>{craftGrid[k]=v;CraftChanged();});}
            matchedRecipe=MainRecipeCatalog.Match(craftGrid,craftSize);Rect outR=new Rect(gx+craftSize*cell+90,gy+(craftSize-1)*cell*.5f,64,64);GUI.Label(new Rect(outR.x-50,outR.y+18,45,22),"→");
            MainInventoryStackData result=matchedRecipe?.CreateOutput();DrawStack(outR,result,true);if(result!=null&&Event.current.type==EventType.MouseDown&&outR.Contains(Event.current.mousePosition)){if(TryReceiveCraftOutput(result)){ConsumeCraftOnce();Event.current.Use();}}
        }
        void CraftChanged(){matchedRecipe=MainRecipeCatalog.Match(craftGrid,craftSize);MarkDirty();}
        bool TryReceiveCraftOutput(MainInventoryStackData output)
        {
            if(cursor==null){cursor=output.Clone();return true;}if(!MainSurvivalInventory.SameIdentity(cursor,output)||cursor.dur>=0||output.dur>=0)return false;int max=MainInventoryCatalog.MaxStack(cursor);if(cursor.count+output.count>max)return false;cursor.count+=output.count;return true;
        }
        void ConsumeCraftOnce(){int n=craftSize*craftSize;for(int i=0;i<n;i++)if(craftGrid[i]!=null)ConsumeOne(ref craftGrid[i]);matchedRecipe=MainRecipeCatalog.Match(craftGrid,craftSize);MarkDirty();}

        void DrawFurnace(float x,float y,float w,float h)
        {
            FurnaceState f=GetFurnace(openedBlock,true);float cx=x+w*.5f-160,cy=y+72;
            DrawInteractiveSlot(new Rect(cx,cy,58,58),()=>f.inp,v=>{f.inp=v;MarkDirty();});
            DrawInteractiveSlot(new Rect(cx,cy+82,58,58),()=>f.fuel,v=>{f.fuel=v;MarkDirty();},false,v=>v==null||MainFurnaceCatalog.FuelSeconds(v)>0f);
            Rect outR=new Rect(cx+240,cy+40,58,58);DrawInteractiveSlot(outR,()=>f.outp,v=>{f.outp=v;MarkDirty();},true);
            GUI.Label(new Rect(cx+78,cy+8,130,25),"Smelt "+Mathf.RoundToInt(f.prog*100f)+"%");GUI.HorizontalSlider(new Rect(cx+78,cy+36,120,20),f.prog,0f,1f);
            float bp=f.burnMax>0?f.burn/f.burnMax:0f;GUI.Label(new Rect(cx+78,cy+86,130,25),"Fuel "+Mathf.RoundToInt(bp*100f)+"%");GUI.HorizontalSlider(new Rect(cx+78,cy+114,120,20),bp,0f,1f);
        }
        void DrawChest(float x,float y,float w,float h)
        {
            List<Vector3Int> secs=ChestSections(openedBlock);float cell=48,cx=x+(w-(9*cell))/2,cy=y+58;for(int s=0;s<secs.Count;s++){MainInventoryStackData[] arr=GetChest(secs[s],true);for(int i=0;i<27;i++){int si=i;DrawInteractiveSlot(new Rect(cx+(i%9)*cell,cy+(s*3+i/9)*cell,44,44),()=>arr[si],v=>{arr[si]=v;MarkDirty();});}}
        }
        void DrawPlayerInventory(float x,float y,float width)
        {
            float cell=Mathf.Min(52,(width-16)/9f);GUI.Label(new Rect(x,y-26,width,24),"Inventory / Инвентарь");for(int row=0;row<4;row++)for(int col=0;col<9;col++){int slot=row==3?col:9+row*9+col;int si=slot;DrawInteractiveSlot(new Rect(x+col*cell,y+row*cell,cell-4,cell-4),()=>player.SurvivalInventory.Get(si),v=>{player.SurvivalInventory.Set(si,v);player.NotifyInventoryChanged();MarkDirty();});}
        }
        void DrawInteractiveSlot(Rect r,Func<MainInventoryStackData> get,Action<MainInventoryStackData> set,bool takeOnly=false,Func<MainInventoryStackData,bool> accepts=null)
        {
            MainInventoryStackData s=get();DrawStack(r,s,true);Event e=Event.current;if(e.type!=EventType.MouseDown||!r.Contains(e.mousePosition))return;
            if(e.button==0)
            {
                if(cursor==null){if(s!=null){cursor=s;set(null);}}
                else if(!takeOnly&&(accepts==null||accepts(cursor)))
                {
                    if(s==null){set(cursor);cursor=null;}
                    else if(MainSurvivalInventory.SameIdentity(s,cursor)&&s.dur<0&&cursor.dur<0){int max=MainInventoryCatalog.MaxStack(s),n=Math.Min(max-s.count,cursor.count);if(n>0){s.count+=n;cursor.count-=n;set(s);if(cursor.count<=0)cursor=null;}else{set(cursor);cursor=s;}}
                    else{set(cursor);cursor=s;}
                }
            }
            else if(e.button==1&&!takeOnly)
            {
                if(cursor==null&&s!=null){if(s.dur>=0||s.count==1){cursor=s;set(null);}else{int n=(s.count+1)/2;cursor=s.Clone();cursor.count=n;s.count-=n;set(s.count>0?s:null);}}
                else if(cursor!=null&&(accepts==null||accepts(cursor))){if(s==null){var one=cursor.Clone();one.count=1;set(one);cursor.count--;if(cursor.count<=0)cursor=null;}else if(MainSurvivalInventory.SameIdentity(s,cursor)&&s.dur<0&&cursor.dur<0&&s.count<MainInventoryCatalog.MaxStack(s)){s.count++;set(s);cursor.count--;if(cursor.count<=0)cursor=null;}}
            }
            MarkDirty();e.Use();
        }
        void DrawStack(Rect r,MainInventoryStackData s,bool box)
        {
            if(box)GUI.Box(r,GUIContent.none);if(s==null)return;string label=s.block?((BlockId)s.blockId).ToString():ShortItem(s.itemKey);GUI.Label(new Rect(r.x+2,r.y+2,r.width-4,r.height-4),label,tinyStyle);if(s.count>1)GUI.Label(new Rect(r.x+r.width-22,r.y+r.height-20,20,18),s.count.ToString());if(s.dur>=0)GUI.Label(new Rect(r.x+2,r.y+r.height-18,r.width-4,16),"d:"+s.dur,tinyStyle);
        }
        static string ShortItem(string k){if(string.IsNullOrEmpty(k))return "?";return k.StartsWith("item_",StringComparison.Ordinal)?k.Substring(5).Replace('_',' '):k.Replace('_',' ');}

        public MainFurnaceSaveData[] CaptureFurnacesSource()
        {
            var list=new List<MainFurnaceSaveData>();foreach(var kv in furnaces){FurnaceState f=kv.Value;if(f.inp==null&&f.fuel==null&&f.outp==null)continue;Vector3Int p=kv.Key;list.Add(new MainFurnaceSaveData{x=p.x,y=p.y,z=SourceCoords.UnityBlockZToSource(p.z),inp=Clone(f.inp),fuel=Clone(f.fuel),outp=Clone(f.outp),prog=f.prog,burn=f.burn,burnMax=f.burnMax});}return list.ToArray();
        }
        public MainChestSaveData[] CaptureChestsSource()
        {
            var list=new List<MainChestSaveData>();foreach(var kv in chests){bool any=false;for(int i=0;i<kv.Value.Length;i++)if(kv.Value[i]!=null){any=true;break;}if(!any)continue;Vector3Int p=kv.Key;var copy=new MainInventoryStackData[27];for(int i=0;i<27;i++)copy[i]=Clone(kv.Value[i]);list.Add(new MainChestSaveData{x=p.x,y=p.y,z=SourceCoords.UnityBlockZToSource(p.z),slots=copy});}return list.ToArray();
        }
        void RestoreFurnaces(MainFurnaceSaveData[] data)
        {
            if(data==null)return;for(int i=0;i<data.Length;i++){var d=data[i];if(d==null)continue;Vector3Int p=new Vector3Int(d.x,d.y,SourceCoords.SourceBlockZToUnity(d.z));furnaces[p]=new FurnaceState{inp=Clone(d.inp),fuel=Clone(d.fuel),outp=Clone(d.outp),prog=Mathf.Clamp01(d.prog),burn=Mathf.Max(0,d.burn),burnMax=Mathf.Max(0,d.burnMax),lit=false};}
        }
        void RestoreChests(MainChestSaveData[] data)
        {
            if(data==null)return;for(int i=0;i<data.Length;i++){var d=data[i];if(d==null)continue;var a=new MainInventoryStackData[27];if(d.slots!=null)for(int n=0;n<Math.Min(27,d.slots.Length);n++)a[n]=Clone(d.slots[n]);Vector3Int p=new Vector3Int(d.x,d.y,SourceCoords.SourceBlockZToUnity(d.z));chests[p]=a;}
        }
        static MainInventoryStackData Clone(MainInventoryStackData s)=>s?.Clone();
    }

    public static class MainFurnaceCatalog
    {
        static bool Block(MainInventoryStackData s,BlockId id)=>s!=null&&s.block&&s.blockId==(int)id;
        static bool Item(MainInventoryStackData s,string key)=>s!=null&&!s.block&&s.itemKey==key;
        static MainInventoryStackData B(BlockId id)=>new MainInventoryStackData{block=true,blockId=(int)id,count=1,dur=-1};
        static MainInventoryStackData I(string key)=>new MainInventoryStackData{block=false,itemKey=key,count=1,dur=-1};

        public static float FuelSeconds(MainInventoryStackData s)
        {
            if(s==null)return 0f;
            if(!s.block)
            {
                switch(s.itemKey){case "item_stick":case "item_bowl":return 5f;case "item_coal":return 80f;case "item_lava_bucket":return 1000f;}
                return 0f;
            }
            BlockId id=(BlockId)s.blockId;
            switch(id)
            {
                case BlockId.OakLog:case BlockId.BirchLog:case BlockId.SpruceLog:case BlockId.JungleLog:case BlockId.AcaciaLog:case BlockId.DarkOakLog:case BlockId.CherryWood:
                case BlockId.OakPlanks:case BlockId.SprucePlanks:case BlockId.BirchPlanks:case BlockId.DarkOakPlanks:case BlockId.CraftingTable:return 15f;
                default:return 0f;
            }
        }

        public static MainInventoryStackData SmeltResult(MainInventoryStackData s)
        {
            if(s==null)return null;
            if(!s.block)
            {
                switch(s.itemKey)
                {
                    case "item_raw_copper":return I("item_copper_ingot");case "item_clay_ball":return I("item_brick");
                    case "item_porkchop":return I("item_cooked_porkchop");case "item_beef":return I("item_cooked_beef");case "item_mutton":return I("item_cooked_mutton");case "item_chicken":return I("item_cooked_chicken");case "item_potato":return I("item_baked_potato");case "item_raw_cod":return I("item_cooked_cod");case "item_raw_salmon":return I("item_cooked_salmon");
                    default:return null;
                }
            }
            BlockId id=(BlockId)s.blockId;
            switch(id)
            {
                case BlockId.IronOre:case BlockId.DeepslateIronOre:return I("item_iron_ingot");
                case BlockId.GoldOre:case BlockId.DeepslateGoldOre:return I("item_gold_ingot");
                case BlockId.CopperOre:case BlockId.DeepslateCopperOre:return I("item_copper_ingot");
                case BlockId.Sand:case BlockId.RedSand:return B(BlockId.Glass);
                case BlockId.Cobblestone:return B(BlockId.Stone);
                case BlockId.Cactus:return I("item_green_dye");
                case BlockId.Clay:return B(BlockId.Terracotta);
                case BlockId.OakLog:case BlockId.BirchLog:case BlockId.SpruceLog:case BlockId.JungleLog:case BlockId.AcaciaLog:case BlockId.DarkOakLog:case BlockId.CherryWood:return I("item_coal");
                default:return null;
            }
        }
    }

    public sealed class MainRecipe
    {
        public string[] Shape;
        public MainIngredient[] Items;
        public Dictionary<char,MainIngredient> Keys;
        public bool Mirror;
        public MainInventoryStackData Output;
        public MainInventoryStackData CreateOutput(){var o=Output.Clone();if(!o.block&&o.dur<0){int d=MainInventoryCatalog.DefaultDurability(o.itemKey);if(d>0)o.dur=d;}return o;}
    }
    public sealed class MainIngredient
    {
        readonly Func<MainInventoryStackData,bool> test;
        MainIngredient(Func<MainInventoryStackData,bool> t){test=t;}
        public bool Matches(MainInventoryStackData s)=>s!=null&&test(s);
        public static MainIngredient Block(BlockId id)=>new MainIngredient(s=>s.block&&s.blockId==(int)id);
        public static MainIngredient Item(string key)=>new MainIngredient(s=>!s.block&&s.itemKey==key);
        public static MainIngredient ItemOrMob(string key,MobItemId id)=>new MainIngredient(s=>!s.block&&(s.itemKey==key||s.itemKey==MainInventoryCatalog.MobKey(id)));
        public static MainIngredient AnyPlanks()=>new MainIngredient(s=>s.block&&(s.blockId==(int)BlockId.OakPlanks||s.blockId==(int)BlockId.SprucePlanks||s.blockId==(int)BlockId.BirchPlanks||s.blockId==(int)BlockId.DarkOakPlanks));
        public static MainIngredient AnyCobble()=>new MainIngredient(s=>s.block&&s.blockId==(int)BlockId.Cobblestone);
    }

    /// <summary>Source PA/SE/Ld recipe matcher for the block/item namespace implemented by this port.</summary>
    public static class MainRecipeCatalog
    {
        static readonly List<MainRecipe> recipes=Build();
        public static int RecipeCount=>recipes.Count;
        static MainInventoryStackData B(BlockId id,int n=1)=>new MainInventoryStackData{block=true,blockId=(int)id,count=n,dur=-1};
        static MainInventoryStackData I(string key,int n=1)=>new MainInventoryStackData{block=false,itemKey=key,count=n,dur=-1};
        static MainRecipe Shaped(MainInventoryStackData o,string[] sh,bool mirror,params object[] pairs){var k=new Dictionary<char,MainIngredient>();for(int i=0;i<pairs.Length;i+=2)k[(char)pairs[i]]=(MainIngredient)pairs[i+1];return new MainRecipe{Output=o,Shape=sh,Keys=k,Mirror=mirror};}
        static MainRecipe Shapeless(MainInventoryStackData o,params MainIngredient[] items)=>new MainRecipe{Output=o,Items=items};
        static List<MainRecipe> Build()
        {
            var r=new List<MainRecipe>();MainIngredient P=MainIngredient.AnyPlanks(),C=MainIngredient.AnyCobble();
            r.Add(Shapeless(B(BlockId.OakPlanks,4),MainIngredient.Block(BlockId.OakLog)));r.Add(Shapeless(B(BlockId.SprucePlanks,4),MainIngredient.Block(BlockId.SpruceLog)));r.Add(Shapeless(B(BlockId.BirchPlanks,4),MainIngredient.Block(BlockId.BirchLog)));r.Add(Shapeless(B(BlockId.DarkOakPlanks,4),MainIngredient.Block(BlockId.DarkOakLog)));
            r.Add(Shaped(I("item_stick",4),new[]{"P","P"},false,'P',P));
            r.Add(Shaped(B(BlockId.CraftingTable),new[]{"PP","PP"},false,'P',P));
            r.Add(Shaped(B(BlockId.Furnace),new[]{"CCC","C.C","CCC"},false,'C',C));
            r.Add(Shaped(B(BlockId.Torch,4),new[]{"C","S"},false,'C',MainIngredient.Item("item_coal"),'S',MainIngredient.Item("item_stick")));
            r.Add(Shaped(B(BlockId.Ladder,3),new[]{"S.S","SSS","S.S"},false,'S',MainIngredient.Item("item_stick")));
            r.Add(Shapeless(I("flint_and_steel"),MainIngredient.Item("item_iron_ingot"),MainIngredient.Item("item_flint")));
            r.Add(Shaped(B(BlockId.Chest),new[]{"PPP","P.P","PPP"},false,'P',P));
            r.Add(Shaped(I("item_bread"),new[]{"WWW"},false,'W',MainIngredient.Item("item_wheat")));
            r.Add(Shaped(I("item_bucket"),new[]{"M.M",".M."},false,'M',MainIngredient.Item("item_iron_ingot")));
            r.Add(Shaped(B(BlockId.Rail,16),new[]{"M.M","MSM","M.M"},false,'M',MainIngredient.Item("item_iron_ingot"),'S',MainIngredient.Item("item_stick")));
            r.Add(Shaped(I("item_minecart"),new[]{"M.M","MMM"},false,'M',MainIngredient.Item("item_iron_ingot")));
            r.Add(Shaped(I("item_boat"),new[]{"P.P","PPP"},false,'P',P));
            r.Add(Shaped(I("item_fishing_rod"),new[]{"..S",".ST","S.T"},true,'S',MainIngredient.Item("item_stick"),'T',MainIngredient.Item("item_string")));
            r.Add(Shapeless(I("item_bone_meal",3),MainIngredient.Item("item_bone")));
            r.Add(Shaped(I("item_shears"),new[]{".M","M."},true,'M',MainIngredient.Item("item_iron_ingot")));
            r.Add(Shaped(I("item_bowl",4),new[]{"P.P",".P."},false,'P',P));
            r.Add(Shapeless(I("item_mushroom_stew"),MainIngredient.Item("item_bowl"),MainIngredient.Block(BlockId.RedMushroom),MainIngredient.Block(BlockId.BrownMushroom)));
            r.Add(Shaped(I("item_golden_apple"),new[]{"GGG","GAG","GGG"},false,'G',MainIngredient.Item("item_gold_ingot"),'A',MainIngredient.Item("item_apple")));
            r.Add(Shaped(I("item_golden_carrot"),new[]{"GGG","GCG","GGG"},false,'G',MainIngredient.Item("item_gold_ingot"),'C',MainIngredient.Item("item_carrot")));
            r.Add(Shapeless(I("item_pumpkin_seeds",4),MainIngredient.Block(BlockId.Pumpkin)));r.Add(Shapeless(I("item_melon_seeds"),MainIngredient.Item("item_melon_slice")));
            r.Add(Shapeless(I("item_sugar"),MainIngredient.Block(BlockId.SugarCane)));
            r.Add(Shaped(B(BlockId.Melon),new[]{"MMM","MMM","MMM"},false,'M',MainIngredient.Item("item_melon_slice")));
            r.Add(Shapeless(I("item_pumpkin_pie"),MainIngredient.Block(BlockId.Pumpkin),MainIngredient.Item("item_sugar"),MainIngredient.ItemOrMob("item_egg",MobItemId.Egg)));
            r.Add(Shaped(B(BlockId.Tnt),new[]{"GSG","SGS","GSG"},false,'G',MainIngredient.Item("item_gunpowder"),'S',MainIngredient.Block(BlockId.Sand)));
            r.Add(Shaped(B(BlockId.Sandstone),new[]{"MM","MM"},false,'M',MainIngredient.Block(BlockId.Sand)));r.Add(Shaped(B(BlockId.RedSandstone),new[]{"MM","MM"},false,'M',MainIngredient.Block(BlockId.RedSand)));
            r.Add(Shaped(B(BlockId.CutSandstone,4),new[]{"MM","MM"},false,'M',MainIngredient.Block(BlockId.Sandstone)));r.Add(Shaped(B(BlockId.ChiseledSandstone),new[]{"M","M"},false,'M',MainIngredient.Block(BlockId.Sandstone)));
            r.Add(Shaped(B(BlockId.Clay),new[]{"CC","CC"},false,'C',MainIngredient.Item("item_clay_ball")));r.Add(Shaped(B(BlockId.FlowerPot),new[]{"B.B",".B."},false,'B',MainIngredient.Item("item_brick")));
            AddDyes(r);
            r.Add(Shaped(B(BlockId.GoldBlock),new[]{"XXX","XXX","XXX"},false,'X',MainIngredient.Item("item_gold_ingot")));r.Add(Shapeless(I("item_gold_ingot",9),MainIngredient.Block(BlockId.GoldBlock)));
            AddTools(r,"wooden",P);AddTools(r,"stone",C);AddTools(r,"iron",MainIngredient.Item("item_iron_ingot"));AddTools(r,"golden",MainIngredient.Item("item_gold_ingot"));AddTools(r,"diamond",MainIngredient.Item("item_diamond"));
            AddArmor(r,"leather",MainIngredient.Item("item_leather"));AddArmor(r,"iron",MainIngredient.Item("item_iron_ingot"));AddArmor(r,"golden",MainIngredient.Item("item_gold_ingot"));AddArmor(r,"diamond",MainIngredient.Item("item_diamond"));
            r.Add(Shaped(I("item_arrow",4),new[]{"F","S","E"},false,'F',MainIngredient.Item("item_flint"),'S',MainIngredient.Item("item_stick"),'E',MainIngredient.Item("item_feather")));
            // main.js currently uses FEATHER as the bow's F ingredient; preserve that source behavior.
            r.Add(Shaped(I("item_bow"),new[]{".SF","S.F",".SF"},true,'S',MainIngredient.Item("item_stick"),'F',MainIngredient.Item("item_feather")));
            r.Add(Shaped(B(BlockId.OakDoor,3),new[]{"MM","MM","MM"},false,'M',MainIngredient.Block(BlockId.OakPlanks)));r.Add(Shaped(B(BlockId.OakTrapdoor,2),new[]{"MMM","MMM"},false,'M',MainIngredient.Block(BlockId.OakPlanks)));
            r.Add(Shaped(B(BlockId.BirchDoor,3),new[]{"MM","MM","MM"},false,'M',MainIngredient.Block(BlockId.BirchPlanks)));
            AddWoodShapes(r,BlockId.OakPlanks,BlockId.OakSlab,BlockId.OakStairs,BlockId.OakFence,BlockId.OakFenceGate);AddWoodShapes(r,BlockId.SprucePlanks,BlockId.SpruceSlab,BlockId.SpruceStairs,BlockId.SpruceFence,(BlockId)0);
            AddWoodShapes(r,BlockId.DarkOakPlanks,BlockId.DarkOakSlab,BlockId.DarkOakStairs,BlockId.DarkOakFence,(BlockId)0);
            AddStoneShapes(r,BlockId.Cobblestone,BlockId.CobblestoneSlab,BlockId.CobblestoneStairs,BlockId.CobblestoneWall);
            r.Add(Shaped(B(BlockId.SandstoneSlab,6),new[]{"MMM"},false,'M',MainIngredient.Block(BlockId.Sandstone)));
            r.Add(Shaped(B(BlockId.SandstoneStairs,4),new[]{"M..","MM.","MMM"},true,'M',MainIngredient.Block(BlockId.Sandstone)));
            r.Add(Shaped(B(BlockId.GlassPane,16),new[]{"MMM","MMM"},false,'M',MainIngredient.Block(BlockId.Glass)));
            r.Add(Shapeless(B(BlockId.OakButton),MainIngredient.Block(BlockId.OakPlanks)));
            r.Add(Shaped(B(BlockId.OakPressurePlate),new[]{"MM"},false,'M',MainIngredient.Block(BlockId.OakPlanks)));
            r.Add(Shaped(B(BlockId.StonePressurePlate),new[]{"MM"},false,'M',MainIngredient.Block(BlockId.Stone)));
            return r;
        }
        static void AddDyes(List<MainRecipe> r)
        {
            r.Add(Shapeless(I("item_yellow_dye"),MainIngredient.Block(BlockId.Dandelion)));
            r.Add(Shapeless(I("item_red_dye"),MainIngredient.Block(BlockId.Poppy)));r.Add(Shapeless(I("item_red_dye"),MainIngredient.Block(BlockId.RedTulip)));
            r.Add(Shapeless(I("item_light_blue_dye"),MainIngredient.Block(BlockId.BlueOrchid)));r.Add(Shapeless(I("item_magenta_dye"),MainIngredient.Block(BlockId.Allium)));
            r.Add(Shapeless(I("item_orange_dye"),MainIngredient.Block(BlockId.OrangeTulip)));r.Add(Shapeless(I("item_pink_dye"),MainIngredient.Block(BlockId.PinkTulip)));
            r.Add(Shapeless(I("item_light_gray_dye"),MainIngredient.Block(BlockId.AzureBluet)));r.Add(Shapeless(I("item_light_gray_dye"),MainIngredient.Block(BlockId.WhiteTulip)));r.Add(Shapeless(I("item_light_gray_dye"),MainIngredient.Block(BlockId.OxeyeDaisy)));
            r.Add(Shapeless(I("item_blue_dye"),MainIngredient.Block(BlockId.Cornflower)));r.Add(Shapeless(I("item_white_dye"),MainIngredient.Block(BlockId.LilyOfTheValley)));
            r.Add(Shapeless(I("item_white_dye"),MainIngredient.Item("item_bone_meal")));r.Add(Shapeless(I("item_brown_dye"),MainIngredient.Block(BlockId.BrownMushroom)));
            r.Add(Shapeless(I("item_cyan_dye",2),MainIngredient.Item("item_green_dye"),MainIngredient.Item("item_blue_dye")));r.Add(Shapeless(I("item_gray_dye",2),MainIngredient.Item("item_black_dye"),MainIngredient.Item("item_white_dye")));
            r.Add(Shapeless(I("item_lime_dye",2),MainIngredient.Item("item_green_dye"),MainIngredient.Item("item_white_dye")));r.Add(Shapeless(I("item_purple_dye",2),MainIngredient.Item("item_red_dye"),MainIngredient.Item("item_blue_dye")));
        }
        static void AddArmor(List<MainRecipe> r,string mat,MainIngredient m)
        {
            r.Add(Shaped(I("item_"+mat+"_helmet"),new[]{"MMM","M.M"},false,'M',m));r.Add(Shaped(I("item_"+mat+"_chestplate"),new[]{"M.M","MMM","MMM"},false,'M',m));
            r.Add(Shaped(I("item_"+mat+"_leggings"),new[]{"MMM","M.M","M.M"},false,'M',m));r.Add(Shaped(I("item_"+mat+"_boots"),new[]{"M.M","M.M"},false,'M',m));
        }
        static void AddTools(List<MainRecipe> r,string mat,MainIngredient m)
        {
            MainIngredient s=MainIngredient.Item("item_stick");r.Add(Shaped(I("item_"+mat+"_pickaxe"),new[]{"MMM",".S.",".S."},false,'M',m,'S',s));r.Add(Shaped(I("item_"+mat+"_axe"),new[]{"MM","MS",".S"},true,'M',m,'S',s));r.Add(Shaped(I("item_"+mat+"_shovel"),new[]{"M","S","S"},false,'M',m,'S',s));r.Add(Shaped(I("item_"+mat+"_sword"),new[]{"M","M","S"},false,'M',m,'S',s));r.Add(Shaped(I("item_"+mat+"_hoe"),new[]{"MM",".S",".S"},true,'M',m,'S',s));
        }
        static void AddWoodShapes(List<MainRecipe> r,BlockId mat,BlockId slab,BlockId stairs,BlockId fence,BlockId gate)
        {
            var m=MainIngredient.Block(mat);var s=MainIngredient.Item("item_stick");r.Add(Shaped(B(slab,6),new[]{"MMM"},false,'M',m));r.Add(Shaped(B(stairs,4),new[]{"M..","MM.","MMM"},true,'M',m));r.Add(Shaped(B(fence,3),new[]{"MSM","MSM"},false,'M',m,'S',s));if((int)gate!=0)r.Add(Shaped(B(gate),new[]{"SMS","SMS"},false,'M',m,'S',s));
        }
        static void AddStoneShapes(List<MainRecipe> r,BlockId mat,BlockId slab,BlockId stairs,BlockId wall)
        {var m=MainIngredient.Block(mat);r.Add(Shaped(B(slab,6),new[]{"MMM"},false,'M',m));r.Add(Shaped(B(stairs,4),new[]{"M..","MM.","MMM"},true,'M',m));r.Add(Shaped(B(wall,6),new[]{"MMM","MMM"},false,'M',m));}

        public static MainRecipe Match(MainInventoryStackData[] grid,int size)
        {
            if(grid==null||size<1||grid.Length<size*size)return null;int rows=size,left=size,right=-1,top=rows,bottom=-1,count=0;for(int y=0;y<rows;y++)for(int x=0;x<size;x++)if(grid[y*size+x]!=null){count++;if(x<left)left=x;if(x>right)right=x;if(y<top)top=y;if(y>bottom)bottom=y;}if(count==0)return null;int bw=right-left+1,bh=bottom-top+1;
            for(int ri=0;ri<recipes.Count;ri++)
            {
                MainRecipe r=recipes[ri];if(r.Items!=null)
                {
                    if(r.Items.Length!=count)continue;int active=size*size;var used=new bool[active];bool ok=true;for(int n=0;n<r.Items.Length&&ok;n++){bool found=false;for(int i=0;i<active;i++)if(!used[i]&&r.Items[n].Matches(grid[i])){used[i]=true;found=true;break;}if(!found)ok=false;}if(ok)return r;continue;
                }
                int rw=r.Shape[0].Length,rh=r.Shape.Length;if(rw!=bw||rh!=bh||rw>size||rh>rows)continue;if(MatchShaped(r,grid,size,left,top,false)||r.Mirror&&MatchShaped(r,grid,size,left,top,true))return r;
            }
            return null;
        }
        static bool MatchShaped(MainRecipe r,MainInventoryStackData[] g,int size,int left,int top,bool mirror)
        {
            int w=r.Shape[0].Length,h=r.Shape.Length;for(int y=0;y<h;y++)for(int x=0;x<w;x++){char c=r.Shape[y][mirror?w-1-x:x];MainInventoryStackData s=g[(top+y)*size+left+x];if(c=='.'){if(s!=null)return false;}else if(s==null||!r.Keys[c].Matches(s))return false;}return true;
        }
    }
}
