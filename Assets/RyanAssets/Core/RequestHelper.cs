using System;

namespace RyanAssets.Core {
    public static class RequestHelper {
        public static double GetRetryDelay(int last_try_number) {
            return MathF.Min(30f, MathF.Pow(last_try_number + 1, 2f));
        }
    }
}