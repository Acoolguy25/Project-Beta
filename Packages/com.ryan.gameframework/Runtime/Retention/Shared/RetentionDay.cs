using System;

namespace RyanAssets.Retention {
    /// <summary>
    /// The retention calendar: whole UTC days counted from 1970-01-01, so a "day" is the same on every
    /// server and survives restarts. Daily challenges and the play streak roll over at UTC midnight.
    /// </summary>
    public static class RetentionDay {
        /// <summary>Stands for "never" in saved day fields.</summary>
        public const int None = -1;
        public const int SecondsPerDay = 24 * 60 * 60;

        static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The day number of <paramref name="utc"/> (local times are converted; unspecified ones are taken as UTC).</summary>
        public static int Of(DateTime utc) {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            return (int)Math.Floor((utc - Epoch).TotalDays);
        }

        /// <summary>Midnight UTC that starts <paramref name="day"/>.</summary>
        public static DateTime Start(int day) => Epoch.AddDays(day);

        /// <summary>Whole seconds until the next UTC midnight (1..<see cref="SecondsPerDay"/>).</summary>
        public static int SecondsUntilReset(DateTime utc) {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            double seconds = (Start(Of(utc) + 1) - utc).TotalSeconds;
            return Math.Clamp((int)Math.Ceiling(seconds), 1, SecondsPerDay);
        }
    }
}
