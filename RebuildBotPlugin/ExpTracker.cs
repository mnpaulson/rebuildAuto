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
            if (CurrentBaseExp == -1)
            {
                CurrentBaseExp = exp;
                MaxBaseExp = maxExp;
                return;
            }

            if (exp > CurrentBaseExp)
            {
                long gain = exp - CurrentBaseExp;
                SessionBaseExpGained += gain;
            }
            else if (exp < CurrentBaseExp)
            {
                // Level up occurred: previous level required MaxBaseExp - CurrentBaseExp, plus overflow
                if (MaxBaseExp > 0 && maxExp != MaxBaseExp)
                {
                    long gain = (MaxBaseExp - CurrentBaseExp) + exp;
                    if (gain > 0) SessionBaseExpGained += gain;
                }
            }

            CurrentBaseExp = exp;
            MaxBaseExp = maxExp;
        }

        public void UpdateJobExp(int exp, int maxExp)
        {
            if (CurrentJobExp == -1)
            {
                CurrentJobExp = exp;
                MaxJobExp = maxExp;
                return;
            }

            if (exp > CurrentJobExp)
            {
                long gain = exp - CurrentJobExp;
                SessionJobExpGained += gain;
            }
            else if (exp < CurrentJobExp)
            {
                // Job level up occurred
                if (MaxJobExp > 0 && maxExp != MaxJobExp)
                {
                    long gain = (MaxJobExp - CurrentJobExp) + exp;
                    if (gain > 0) SessionJobExpGained += gain;
                }
            }

            CurrentJobExp = exp;
            MaxJobExp = maxExp;
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

        public TimeSpan? TimeToNextBaseLevel
        {
            get
            {
                if (MaxBaseExp <= 0 || CurrentBaseExp < 0 || BaseExpPerHour <= 0) return null;
                long expRemaining = MaxBaseExp - CurrentBaseExp;
                if (expRemaining <= 0) return TimeSpan.Zero;

                double hours = expRemaining / BaseExpPerHour;
                if (double.IsInfinity(hours) || double.IsNaN(hours) || hours > 999) return null;
                return TimeSpan.FromHours(hours);
            }
        }

        public TimeSpan? TimeToNextJobLevel
        {
            get
            {
                if (MaxJobExp <= 0 || CurrentJobExp < 0 || JobExpPerHour <= 0) return null;
                long expRemaining = MaxJobExp - CurrentJobExp;
                if (expRemaining <= 0) return TimeSpan.Zero;

                double hours = expRemaining / JobExpPerHour;
                if (double.IsInfinity(hours) || double.IsNaN(hours) || hours > 999) return null;
                return TimeSpan.FromHours(hours);
            }
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
