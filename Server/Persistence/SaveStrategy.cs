using System;

using Server.Configuration;

namespace Server
{
    public abstract class SaveStrategy
    {
        public abstract string Name { get; }

        public static SaveStrategy Acquire()
        {
            // Allow config override
            ValierUOConfig.EnsureLoaded();

            string mode = ValierUOConfig.SaveStrategy;

            if (!String.IsNullOrEmpty(mode) && !mode.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                int pc = Core.ProcessorCount;

                switch (mode)
                {
                    case "standard":
                        return new StandardSaveStrategy();

                    case "dual":
                        return Core.MultiProcessor ? (SaveStrategy)new DualSaveStrategy() : new StandardSaveStrategy();

                    case "parallel":
                        if (Core.MultiProcessor && pc > 1)
                            return new ParallelSaveStrategy(pc);

                        return new StandardSaveStrategy();

                    case "dynamic":
                        if (Core.MultiProcessor)
                            return new DynamicSaveStrategy();

                        return new StandardSaveStrategy();

                    default:
                        // fall back to auto selection
                        break;
                }
            }

            // Auto selection (existing behavior)
            if (Core.MultiProcessor)
            {
                int processorCount = Core.ProcessorCount;

#if DynamicSaveStrategy
                if (processorCount > 2)
                {
                    return new DynamicSaveStrategy();
                }
#else
                if (processorCount > 16)
                {
                    return new ParallelSaveStrategy(processorCount);
                }
#endif
                else
                {
                    return new DualSaveStrategy();
                }
            }

            return new StandardSaveStrategy();
        }

        public abstract void Save(SaveMetrics metrics, bool permitBackgroundWrite);

        public abstract void ProcessDecay();
    }
}
