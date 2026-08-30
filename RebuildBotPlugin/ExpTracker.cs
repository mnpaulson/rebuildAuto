using System;

namespace RebuildBotPlugin
{
    public class ExpTracker
    {
        public DateTime SessionStartTime { get; private set; } = DateTime.Now;

        // Base Experience
        public long CurrentBaseExp { get; private set; } = -1;
        public long MaxBaseExp { get; private set; } = -1;
        public long SessionBaseExpGained { get; private set; } = 0;

        // Job Experience
        public long CurrentJobExp { get; private set; } = -1;
        public long MaxJobExp { get; private set; } = -1;
        public long SessionJobExpGained { get; private set; } = 0;

        public void Reset()
        {
            SessionStartTime = DateTime.Now;
            SessionBaseExpGained = 0;
            SessionJobExpGained = 0;
        }

        public void UpdateBaseExp(int exp, int maxExp)
        {
            long current = CurrentBaseExp;
            long max = MaxBaseExp;
            long gained = SessionBaseExpGained;

            UpdateExp(ref current, ref max, ref gained, exp, maxExp);

            CurrentBaseExp = current;
            MaxBaseExp = max;
            SessionBaseExpGained = gained;
        }

        public void UpdateJobExp(int exp, int maxExp)
        {
            long current = CurrentJobExp;
            long max = MaxJobExp;
            long gained = SessionJobExpGained;

            UpdateExp(ref current, ref max, ref gained, exp, maxExp);

            CurrentJobExp = current;
            MaxJobExp = max;
            SessionJobExpGained = gained;
        }

        private static void UpdateExp(ref long currentExp, ref long maxExp, ref long sessionGained, int newExp, int newMaxExp)
        {
            if (currentExp == -1)
            {
                currentExp = newExp;
                maxExp = newMaxExp;
                return;
            }

            if (newExp > currentExp)
            {
                sessionGained += (newExp - currentExp);
            }
            else if (newExp < currentExp)
            {
                // Level up occurred: previous level required (maxExp - currentExp), plus new overflow
                if (maxExp > 0 && newMaxExp != maxExp)
                {
                    long gain = (maxExp - currentExp) + newExp;
                    if (gain > 0) sessionGained += gain;
                }
            }

            currentExp = newExp;
            maxExp = newMaxExp;
        }

        public double ElapsedHours
        {
            get
            {
                double totalHours = (DateTime.Now - SessionStartTime).TotalHours;
                return Math.Max(totalHours, 1.0 / 3600.0); // Minimum 1 second to avoid div by zero
            }
        }

        public TimeSpan ElapsedTime => DateTime.Now - SessionStartTime;

        public double BaseExpPerHour => SessionBaseExpGained / ElapsedHours;
        public double JobExpPerHour => SessionJobExpGained / ElapsedHours;

        public TimeSpan? TimeToNextBaseLevel => CalculateTtl(CurrentBaseExp, MaxBaseExp, BaseExpPerHour);
        public TimeSpan? TimeToNextJobLevel => CalculateTtl(CurrentJobExp, MaxJobExp, JobExpPerHour);

        private static TimeSpan? CalculateTtl(long currentExp, long maxExp, double expPerHour)
        {
            if (maxExp <= 0 || currentExp < 0 || expPerHour <= 0) return null;
            long expRemaining = maxExp - currentExp;
            if (expRemaining <= 0) return TimeSpan.Zero;

            double hours = expRemaining / expPerHour;
            if (double.IsInfinity(hours) || double.IsNaN(hours) || hours > 999) return null;
            return TimeSpan.FromHours(hours);
        }

        public static string FormatExp(double exp)
        {
            if (exp >= 1_000_000)
                return $"{(exp / 1_000_000.0):F2}M";
            if (exp >= 1_000)
                return $"{(exp / 1_000.0):F1}k";
            return $"{exp:N0}";
        }

        public static string FormatTtl(TimeSpan? ttl)
        {
            if (!ttl.HasValue) return "--";
            var t = ttl.Value;
            if (t.TotalHours >= 24)
                return $"{t.Days}d {t.Hours}h";
            if (t.TotalHours >= 1)
                return $"{t.Hours}h {t.Minutes}m";
            return $"{t.Minutes}m {t.Seconds:D2}s";
        }
    }
}
