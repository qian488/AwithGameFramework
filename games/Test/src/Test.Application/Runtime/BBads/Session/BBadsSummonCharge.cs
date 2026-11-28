using System;

namespace Test.Application.BBads.Session
{
    public static class BBadsSummonCharge
    {
        public static (float Remaining, int Summons) Apply(float progress, float amount, float fishCost)
        {
            if (!float.IsFinite(progress) || progress < 0f)
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (!float.IsFinite(amount) || amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (!float.IsFinite(fishCost))
                throw new ArgumentOutOfRangeException(nameof(fishCost));

            float required = Math.Max(1f, fishCost);
            float remaining = progress + amount;
            double fullCharges = Math.Floor((double)remaining / required);
            if (fullCharges > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(amount));

            return (remaining % required, (int)fullCharges);
        }
    }
}
