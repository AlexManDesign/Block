using System;

namespace BlockcraftPort
{
    /// <summary>
    /// Exact deepslate multi-noise biome lookup data exported from main.js' packed A4 table.
    /// The source table contains 7,593 biome hyper-rectangles. To keep generation fast, the
    /// source gQ() search tree is prebuilt offline and stored as biome_multinoise.bytes.
    /// Queries use the same squared distance-to-interval pruning as main's UQ().
    /// </summary>
    public static class MainBiomeLookup
    {
        struct Node
        {
            public short Biome;
            public ushort ChildCount;
            public int ChildStart;
            public short A0, A1, B0, B1, C0, C1, D0, D1, E0, E1, F0, F1, G0, G1;
        }

        static Node[] nodes;
        static int[] childIndices;
        [ThreadStatic] static int lastBest;
        [ThreadStatic] static bool hasLastBest;

        // main's $C -> Jv -> V mapping. Cave/deep variants collapse to the same surface IDs exactly
        // as the source does before returning pv[f].
        static readonly BiomeId[] SourceBiomeMap =
        {
            BiomeId.Badlands, BiomeId.BambooJungle, BiomeId.Beach, BiomeId.BirchForest, BiomeId.CherryGrove,
            BiomeId.ColdOcean, BiomeId.DarkForest, BiomeId.ColdOcean, BiomeId.StonyPeaks, BiomeId.FrozenOcean,
            BiomeId.LukewarmOcean, BiomeId.Ocean, BiomeId.Desert, BiomeId.StonyPeaks, BiomeId.Badlands,
            BiomeId.FlowerForest, BiomeId.Forest, BiomeId.FrozenOcean, BiomeId.FrozenPeaks, BiomeId.FrozenRiver,
            BiomeId.Grove, BiomeId.IceSpikes, BiomeId.JaggedPeaks, BiomeId.Jungle, BiomeId.LukewarmOcean,
            BiomeId.Forest, BiomeId.MangroveSwamp, BiomeId.Meadow, BiomeId.MushroomFields, BiomeId.Ocean,
            BiomeId.BirchForest, BiomeId.OldGrowthTaiga, BiomeId.OldGrowthTaiga, BiomeId.Plains, BiomeId.River,
            BiomeId.Savanna, BiomeId.Savanna, BiomeId.SnowyBeach, BiomeId.Snowy, BiomeId.SnowySlopes,
            BiomeId.SnowyTaiga, BiomeId.SparseJungle, BiomeId.StonyPeaks, BiomeId.StonyShore, BiomeId.Plains,
            BiomeId.Swamp, BiomeId.Taiga, BiomeId.WarmOcean, BiomeId.Windswept, BiomeId.Windswept,
            BiomeId.Windswept, BiomeId.Savanna, BiomeId.WoodedBadlands
        };

        public static bool IsReady => nodes != null && nodes.Length != 0;

        public static void Initialize(byte[] data)
        {
            if (IsReady || data == null || data.Length < 16) return;
            if (data[0] != (byte)'B' || data[1] != (byte)'C' || data[2] != (byte)'B' || data[3] != (byte)'T')
                throw new InvalidOperationException("Invalid biome multi-noise table magic.");

            int o = 4;
            int version = ReadInt(data, ref o);
            if (version != 1) throw new InvalidOperationException("Unsupported biome table version: " + version);
            int nodeCount = ReadInt(data, ref o);
            int edgeCount = ReadInt(data, ref o);
            if (nodeCount <= 0 || nodeCount > 100000 || edgeCount < 0 || edgeCount > 200000)
                throw new InvalidOperationException("Invalid biome multi-noise table sizes.");

            var n = new Node[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                Node x = new Node();
                x.Biome = ReadShort(data, ref o); x.ChildCount = ReadUShort(data, ref o); x.ChildStart = ReadInt(data, ref o);
                x.A0 = ReadShort(data, ref o); x.A1 = ReadShort(data, ref o); x.B0 = ReadShort(data, ref o); x.B1 = ReadShort(data, ref o);
                x.C0 = ReadShort(data, ref o); x.C1 = ReadShort(data, ref o); x.D0 = ReadShort(data, ref o); x.D1 = ReadShort(data, ref o);
                x.E0 = ReadShort(data, ref o); x.E1 = ReadShort(data, ref o); x.F0 = ReadShort(data, ref o); x.F1 = ReadShort(data, ref o);
                x.G0 = ReadShort(data, ref o); x.G1 = ReadShort(data, ref o);
                n[i] = x;
            }
            var e = new int[edgeCount];
            for (int i = 0; i < edgeCount; i++) e[i] = ReadInt(data, ref o);
            if (o != data.Length) throw new InvalidOperationException("Biome table length mismatch.");
            nodes = n; childIndices = e;
            hasLastBest = false;
        }

        public static BiomeId Lookup(float temperature, float humidity, float continentalness, float erosion, float weirdness)
            => Lookup((double)temperature,(double)humidity,(double)continentalness,(double)erosion,(double)weirdness);

        public static BiomeId Lookup(double temperature, double humidity, double continentalness, double erosion, double weirdness)
        {
            if (!IsReady) throw new InvalidOperationException("Packed main biome table is not initialized.");

            // genWorker fQ() expands every int16 endpoint to Float64 / 1e4, then N4()/O4()
            // compares unscaled Float64 climate values. Mirror that arithmetic order exactly.
            double a = Clamp(temperature);
            double b = Clamp(humidity * .82d);
            double c = ContinentalTransform(continentalness);
            double d = Clamp(erosion * 1.5d + (temperature > .5d ? .42d : 0d));
            double e = 0d;
            double f = Clamp(weirdness * 3.5d);
            if (f > -.12d && f < .12d) f = f < 0d ? -.12d : .12d;
            double g = 0d;

            int best = hasLastBest ? lastBest : -1;
            best = Query(0, a, b, c, d, e, f, g, best);
            if (best < 0) throw new InvalidOperationException("Main biome lookup returned no candidate.");
            lastBest = best; hasLastBest = true;
            int sourceIndex = nodes[best].Biome;
            if ((uint)sourceIndex >= (uint)SourceBiomeMap.Length) throw new InvalidOperationException("Invalid source biome index: " + sourceIndex);
            return SourceBiomeMap[sourceIndex];
        }

        static int Query(int nodeIndex, double a, double b, double c, double d, double e, double f, double g, int best)
        {
            Node node = nodes[nodeIndex];
            if (node.ChildCount == 0) return nodeIndex;

            double bestDistance = best >= 0 ? Distance(nodes[best], a, b, c, d, e, f, g) : double.PositiveInfinity;
            int bestNode = best;
            int end = node.ChildStart + node.ChildCount;
            for (int p = node.ChildStart; p < end; p++)
            {
                int childIndex = childIndices[p];
                Node child = nodes[childIndex];
                double childDistance = Distance(child, a, b, c, d, e, f, g);
                if (bestDistance <= childDistance) continue;

                int candidate = Query(childIndex, a, b, c, d, e, f, g, bestNode);
                if (candidate < 0) continue;
                double candidateDistance = candidate == childIndex ? childDistance : Distance(nodes[candidate], a, b, c, d, e, f, g);
                if (candidateDistance == 0f) return candidate;
                if (bestDistance > candidateDistance) { bestDistance = candidateDistance; bestNode = candidate; }
            }
            return bestNode;
        }

        static double Distance(Node n, double a, double b, double c, double d, double e, double f, double g)
        {
            return IntervalDistance(n.A0, n.A1, a) + IntervalDistance(n.B0, n.B1, b) + IntervalDistance(n.C0, n.C1, c) +
                   IntervalDistance(n.D0, n.D1, d) + IntervalDistance(n.E0, n.E1, e) + IntervalDistance(n.F0, n.F1, f) +
                   IntervalDistance(n.G0, n.G1, g);
        }

        static double IntervalDistance(short min, short max, double v)
        {
            double lo=min/10000d, hi=max/10000d;
            double q = v - hi;
            if (q > 0d) return q * q;
            q = lo - v;
            return q > 0d ? q * q : 0d;
        }

        static double Clamp(double v) => v < -1d ? -1d : v > 1d ? 1d : v;
        static readonly double[,] ContinentalCurve = { {-1d,-1d},{-.325d,-.8d},{-.11d,-.19d},{-.077d,-.13d},{-.04d,-.06d},{.05d,.05d},{.37d,.25d},{.8d,.7d},{1.2d,1d} };
        static double ContinentalTransform(double v)
        {
            // main uses fi(Rv, e < .05 ? .05 : e).
            double x = v < .05d ? .05d : v;
            if (x <= ContinentalCurve[0,0]) return ContinentalCurve[0,1];
            int count = ContinentalCurve.GetLength(0);
            for (int i = 1; i < count; i++)
                if (x <= ContinentalCurve[i,0])
                {
                    double t = (x - ContinentalCurve[i-1,0]) / (ContinentalCurve[i,0] - ContinentalCurve[i-1,0]);
                    return ContinentalCurve[i-1,1] + (ContinentalCurve[i,1] - ContinentalCurve[i-1,1]) * t;
                }
            return ContinentalCurve[count-1,1];
        }

        static short ReadShort(byte[] d, ref int o) { int v = d[o] | (d[o+1] << 8); o += 2; return unchecked((short)v); }
        static ushort ReadUShort(byte[] d, ref int o) { int v = d[o] | (d[o+1] << 8); o += 2; return (ushort)v; }
        static int ReadInt(byte[] d, ref int o) { int v = d[o] | (d[o+1] << 8) | (d[o+2] << 16) | (d[o+3] << 24); o += 4; return v; }
    }
}
