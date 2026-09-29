namespace BlockcraftPort
{
    /// <summary>
    /// Source main.js Z6()/zc() mining constants for every reference block.
    /// R48 connects these source class/tier rules to the survival inventory and tool durability path.
    /// </summary>
    public static class BlockMiningRules
    {
        public enum ToolClass : byte { None, Pickaxe, Axe, Shovel }
        public readonly struct Rule
        {
            public readonly float Hardness; public readonly ToolClass Class; public readonly int Tier; public readonly bool NoDrop;
            public Rule(float hardness,ToolClass cls=ToolClass.None,int tier=0,bool noDrop=false)
            { Hardness=hardness;Class=cls;Tier=tier;NoDrop=noDrop; }
            public bool CanDropByHand => !NoDrop && (Class==ToolClass.None || Tier==0);
            // main zc(): C = hard * (canDrop || noDrop ? 1.5 : 5)
            public float HandWork => Hardness * (CanDropByHand || NoDrop ? 1.5f : 5f);
        }

        public static bool Matches(MainToolKind tool,ToolClass required)
        {
            if(required==ToolClass.None)return false;
            switch(required)
            {
                case ToolClass.Pickaxe:return tool==MainToolKind.Pickaxe;
                case ToolClass.Axe:return tool==MainToolKind.Axe;
                case ToolClass.Shovel:return tool==MainToolKind.Shovel;
                default:return false;
            }
        }

        /// <summary>
        /// main.js Z6() for every block: the table is Z6 evaluated by the reference itself
        /// (tools/refgame, SourceBlockData.Hardness/ToolClass/ToolTier/NoDrop). Fluids and bedrock
        /// stay unbreakable here because the raycast never targets them.
        /// </summary>
        public static Rule Get(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsFluid(id)||id==BlockId.Bedrock)return new Rule(-1f);
            int i=(int)id;
            if((uint)i>=(uint)SourceBlockData.Count)return new Rule(1f);
            return new Rule(SourceBlockData.Hardness[i],(ToolClass)SourceBlockData.ToolClass[i],SourceBlockData.ToolTier[i],SourceBlockData.NoDrop[i]);
        }
    }
}
