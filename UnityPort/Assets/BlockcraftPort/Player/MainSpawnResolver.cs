using System;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// Exact startup spawn search from main.js JI()/Jg()/VI()/Bl()/pg().
    /// The search runs after the startup generation square is complete and before lighting/meshing,
    /// so the first rendered frame is already centered on the final dry spawn instead of teleporting
    /// the player after the world becomes visible.
    /// All search coordinates are source-space; Z is reflected only when querying VoxelWorld.
    /// </summary>
    public static class MainSpawnResolver
    {
        const int WorldMin=-65536;
        const int WorldMax=65536;
        const int LocalSearchRadius=48;
        const int FallbackSearchRadius=3000;

        static BlockId GetSource(VoxelWorld world,int sourceX,int y,int sourceZ)
        {
            return world.GetBlock(sourceX,y,SourceCoords.SourceBlockZToUnity(sourceZ));
        }

        // main.js Ae(): source collision-solid excludes fluids, cross vegetation and every
        // non-cube/special shape. Spawn VI() applies additional leaf/log exclusions below.
        static bool SourceSolid(BlockId id)
        {
            if(id==BlockId.Air||BlockRegistry.IsWater(id)||BlockRegistry.IsLava(id))return false;
            BlockShape shape=BlockRegistry.Get(id).Shape;
            return shape==BlockShape.Cube;
        }

        // main.js JI(A): AIR || (!Ae(A) && !EA(A) && !NA(A)).
        // Lava is intentionally accepted by JI in the source; keep that exact predicate here.
        static bool SourceHeadroom(BlockId id)
        {
            return id==BlockId.Air||(!SourceSolid(id)&&!BlockRegistry.IsWater(id)&&!BlockRegistry.IsAquatic(id));
        }

        // main.js Jg(): only these six classic logs are rejected as spawn ground.
        static bool SourceClassicLog(BlockId id)
        {
            return id==BlockId.OakLog||id==BlockId.BirchLog||id==BlockId.SpruceLog||
                   id==BlockId.DarkOakLog||id==BlockId.JungleLog||id==BlockId.AcaciaLog;
        }

        // main.js VI(x,y,z): dry solid support, not leaves/cross/log, plus two valid head cells.
        public static bool IsValidDryStand(VoxelWorld world,int sourceX,int y,int sourceZ)
        {
            BlockId ground=GetSource(world,sourceX,y,sourceZ);
            if(!SourceSolid(ground)||BlockRegistry.IsWater(ground)||BlockRegistry.IsClassicLeaf(ground)||
               BlockRegistry.Get(ground).Shape==BlockShape.Cross||SourceClassicLog(ground))return false;
            return SourceHeadroom(GetSource(world,sourceX,y+1,sourceZ))&&
                   SourceHeadroom(GetSource(world,sourceX,y+2,sourceZ));
        }

        // main.js Bl(): coarse land search. This is normally only the pg() fallback because the
        // first-world metadata already supplies SJ. It uses biome height and density-surface height.
        static bool TryFindCoarseLand(BiomeGenerator biomes,int sourceX,int sourceZ,out Vector2Int land)
        {
            for(int radius=0;radius<=FallbackSearchRadius;radius+=radius<200?1:12)
            {
                int step=radius<200?1:12;
                int samples=Math.Max(1,(int)Math.Ceiling(2d*Math.PI*radius/step));
                for(int n=0;n<samples;n++)
                {
                    double angle=n*2d*Math.PI/samples;
                    int x=sourceX+SourceMath.JsRound(Math.Cos(angle)*radius);
                    int z=sourceZ+SourceMath.JsRound(Math.Sin(angle)*radius);
                    if(x<WorldMin||x>=WorldMax||z<WorldMin||z>=WorldMax)continue;
                    if(biomes.Sample(x,z).Height>=VoxelConstants.SeaLevel&&
                       biomes.DensitySurfaceY(x,z)>=VoxelConstants.SeaLevel)
                    {
                        land=new Vector2Int(x,z);return true;
                    }
                }
            }
            land=default(Vector2Int);return false;
        }

        // main startup before generation: an explicit metadata spawn is used directly only when
        // be(x,z).h >= sea; otherwise Bl() chooses the nearest coarse land anchor.
        public static Vector2Int ResolveGenerationAnchor(BiomeGenerator biomes,int sourceX,int sourceZ)
        {
            if(biomes==null)return new Vector2Int(sourceX,sourceZ);
            if(biomes.Sample(sourceX,sourceZ).Height>=VoxelConstants.SeaLevel)return new Vector2Int(sourceX,sourceZ);
            Vector2Int land;return TryFindCoarseLand(biomes,sourceX,sourceZ,out land)?land:new Vector2Int(sourceX,sourceZ);
        }

        /// <summary>
        /// main.js pg(sourceX,sourceZ), including its post-search wet-cell safety pass.
        /// Returns a Unity feet position whose source-space center is [x+.5, y, z+.5].
        /// </summary>
        public static Vector3 ResolveNewWorldSpawn(VoxelWorld world,int sourceX,int sourceZ)
        {
            if(world==null||world.Generator==null)
                return new Vector3(sourceX+.5f,VoxelConstants.SeaLevel+1f,SourceCoords.SourceWorldZToUnity(sourceZ+.5f));

            BiomeGenerator biomes=world.Generator.Biomes;
            int finalX=sourceX,finalZ=sourceZ,feetY=VoxelConstants.SeaLevel+1;
            bool found=false;

            for(int radius=0;radius<=LocalSearchRadius&&!found;radius++)
            {
                int samples=Math.Max(1,radius*8);
                for(int n=0;n<samples&&!found;n++)
                {
                    double angle=n*2d*Math.PI/samples;
                    int x=sourceX+SourceMath.JsRound(Math.Cos(angle)*radius);
                    int z=sourceZ+SourceMath.JsRound(Math.Sin(angle)*radius);
                    BiomeSample sample=biomes.Sample(x,z);
                    if(sample.Height<VoxelConstants.SeaLevel)continue;
                    int densitySurface=biomes.DensitySurfaceY(x,z);
                    for(int y=VoxelConstants.MaxY-2;y>=VoxelConstants.MinY+1;y--)
                    {
                        if(!IsValidDryStand(world,x,y,z))continue;
                        // main pg(): if the first valid cell is more than six blocks below b1(),
                        // abandon this column rather than spawning in a deep cave.
                        if(y<densitySurface-6)break;
                        finalX=x;finalZ=z;feetY=y+1;found=true;break;
                    }
                }
            }

            if(!found)
            {
                Vector2Int land;
                if(TryFindCoarseLand(biomes,sourceX,sourceZ,out land))
                {
                    finalX=land.x;finalZ=land.y;feetY=biomes.DensitySurfaceY(finalX,finalZ)+1;
                }
                else
                {
                    finalX=sourceX;finalZ=sourceZ;feetY=VoxelConstants.SeaLevel+1;
                }
            }

            // main startup Y(): even after pg(), guard against feet/head being water/aquatic and
            // re-scan the same column top-down using VI(). This is not a heuristic; it is source code.
            BlockId feet=GetSource(world,finalX,feetY,finalZ);
            BlockId head=GetSource(world,finalX,feetY+1,finalZ);
            if(BlockRegistry.IsWater(feet)||BlockRegistry.IsAquatic(feet)||
               BlockRegistry.IsWater(head)||BlockRegistry.IsAquatic(head))
            {
                for(int y=VoxelConstants.MaxY-2;y>=VoxelConstants.MinY+1;y--)
                {
                    if(IsValidDryStand(world,finalX,y,finalZ)){feetY=y+1;break;}
                }
            }

            return new Vector3(finalX+.5f,feetY,SourceCoords.SourceWorldZToUnity(finalZ+.5f));
        }
    }
}
