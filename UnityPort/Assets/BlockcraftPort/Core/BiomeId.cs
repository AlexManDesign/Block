namespace BlockcraftPort
{
    // Kept aligned with the biome set found in the JS generator.
    public enum BiomeId : byte
    {
        Ocean = 0, Beach = 1, Desert = 2, Savanna = 3, Plains = 4, Forest = 5,
        BirchForest = 6, Taiga = 7, Snowy = 8, Mountain = 9, Peaks = 10, River = 11,
        SnowyBeach = 12, FlowerForest = 13, DarkForest = 14, Jungle = 15, SparseJungle = 16,
        OldGrowthTaiga = 17, SnowyTaiga = 18, Meadow = 19, Grove = 20, SnowySlopes = 21,
        JaggedPeaks = 22, StonyPeaks = 23, Windswept = 24, Badlands = 25, Swamp = 26,
        FrozenOcean = 27, ColdOcean = 28, LukewarmOcean = 29, WarmOcean = 30,
        MushroomFields = 31, BambooJungle = 32, CherryGrove = 33, MangroveSwamp = 34,
        WoodedBadlands = 35, FrozenPeaks = 36, FrozenRiver = 37, IceSpikes = 38, StonyShore = 39
    }

    public struct BiomeSample
    {
        public int Height;
        public BiomeId Biome;
        public float Temperature, Humidity, Continentalness, Erosion, Weirdness;
        // main iA() returns the discretized climate buckets plus T1() terrain coefficients.
        public byte TemperatureBucket, HumidityBucket;
        public float Factor, MountainAmplitude;
        // Source iA()/T1() runs as JavaScript Number (IEEE-754 double).  Keep the compact float
        // fields for gameplay/debug consumers, but retain the exact coefficients used by terrain
        // density so classification and zero-crossings are not changed by an early float cast.
        public double SourceFactor, SourceMountainAmplitude;
        public bool SourceHeightInvalid;

        public BiomeSample(int h, BiomeId b, double t, double hu, double c, double e, double w, double factor, double mAmp, bool sourceHeightInvalid=false)
        {
            Height=h; Biome=b; Temperature=(float)t; Humidity=(float)hu; Continentalness=(float)c; Erosion=(float)e; Weirdness=(float)w;
            TemperatureBucket=(byte)(t<-.45?0:t<-.15?1:t<.2?2:t<.55?3:4);
            HumidityBucket=(byte)(hu<-.35?0:hu<-.1?1:hu<.1?2:hu<.3?3:4);
            Factor=(float)factor; MountainAmplitude=(float)mAmp;
            SourceFactor=factor; SourceMountainAmplitude=mAmp; SourceHeightInvalid=sourceHeightInvalid;
        }
    }
}
