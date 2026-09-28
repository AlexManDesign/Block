using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Lightweight source-style startup mode choice. It exists only before the world starts and is
    /// destroyed after selection, so it adds no steady-state render/UI cost.
    /// </summary>
    public sealed class MainModeSelector : MonoBehaviour
    {
        public PortBootstrap Bootstrap;
        GUIStyle titleStyle, hintStyle, buttonStyle, boxStyle;
        bool choosing;
        MainWorldSaveData saved;
        bool hasSaved;

        void Awake()
        {
            Cursor.lockState=CursorLockMode.None;
            Cursor.visible=true;
            hasSaved=MainWorldSave.TryLoad(out saved);
        }

        void Update()
        {
            if(choosing||Bootstrap==null)return;
            if(hasSaved&&(Input.GetKeyDown(KeyCode.C)||Input.GetKeyDown(KeyCode.Return)))ContinueSaved();
            else if(Input.GetKeyDown(KeyCode.Alpha1)||Input.GetKeyDown(KeyCode.Keypad1))ChooseNew(MainGameMode.Survival);
            else if(Input.GetKeyDown(KeyCode.Alpha2)||Input.GetKeyDown(KeyCode.Keypad2))ChooseNew(MainGameMode.Creative);
        }

        void EnsureStyles()
        {
            if(titleStyle!=null)return;
            titleStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=24,fontStyle=FontStyle.Bold};
            hintStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=13};
            buttonStyle=new GUIStyle(GUI.skin.button){fontSize=18,fontStyle=FontStyle.Bold};
            boxStyle=new GUIStyle(GUI.skin.box){padding=new RectOffset(22,22,22,22)};
        }

        void OnGUI()
        {
            if(choosing||Bootstrap==null)return;
            EnsureStyles();
            float w=Mathf.Min(560f,Screen.width-32f), h=hasSaved?340f:250f;
            Rect panel=new Rect((Screen.width-w)*.5f,(Screen.height-h)*.5f,w,h);
            GUI.Box(panel,GUIContent.none,boxStyle);
            GUI.Label(new Rect(panel.x+15,panel.y+18,panel.width-30,40),hasSaved?"Продолжить или новый мир / Continue or new world":"Выбери режим игры / Choose game mode",titleStyle);
            float y=64f;
            if(hasSaved)
            {
                string mode=saved.mode==(int)MainGameMode.Survival?"Survival":"Creative";
                GUI.Label(new Rect(panel.x+15,panel.y+y,panel.width-30,26),"Сохранённый мир: "+mode+"   C / Enter — продолжить",hintStyle);
                if(GUI.Button(new Rect(panel.x+20,panel.y+y+32,panel.width-40,58),"CONTINUE / ПРОДОЛЖИТЬ",buttonStyle))ContinueSaved();
                y+=102f;
            }
            GUI.Label(new Rect(panel.x+15,panel.y+y,panel.width-30,24),"Новый мир удалит текущий save / New world replaces current save",hintStyle);
            float bw=(panel.width-58f)*.5f;
            if(GUI.Button(new Rect(panel.x+20,panel.y+y+38,bw,72),"1  SURVIVAL\nНовый мир",buttonStyle))ChooseNew(MainGameMode.Survival);
            if(GUI.Button(new Rect(panel.x+38+bw,panel.y+y+38,bw,72),"2  CREATIVE\nНовый мир",buttonStyle))ChooseNew(MainGameMode.Creative);
            GUI.Label(new Rect(panel.x+15,panel.y+y+119,panel.width-30,28),"Save: sparse edits + позиция/время; полный inventory переносится отдельно.",hintStyle);
        }

        void ContinueSaved()
        {
            if(choosing||Bootstrap==null||!hasSaved||saved==null)return;
            choosing=true;
            var mode=saved.mode==(int)MainGameMode.Survival?MainGameMode.Survival:MainGameMode.Creative;
            Bootstrap.BeginGame(mode,saved);
            Destroy(gameObject);
        }

        void ChooseNew(MainGameMode mode)
        {
            if(choosing||Bootstrap==null)return;
            choosing=true;
            MainWorldSave.Delete();
            Bootstrap.BeginGame(mode,null);
            Destroy(gameObject);
        }

    }
}
