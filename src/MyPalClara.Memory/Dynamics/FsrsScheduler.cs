namespace MyPalClara.Memory.Dynamics;

/// <summary>
/// FSRS-6 spaced repetition scheduler. Pure static methods, no state.
/// Computes retrievability, memory scores, and grade inference for the Rook memory system.
/// </summary>
public static class FsrsScheduler
{
    // Grade constants
    public const int Again = 1;
    public const int Hard = 2;
    public const int Good = 3;
    public const int Easy = 4;

    // FSRS-6 default parameters (21 weights)
    private static readonly double[] W =
    [
        0.212,    // w0:  initial stability for Again
        1.2931,   // w1:  initial stability for Hard
        2.3065,   // w2:  initial stability for Good
        8.2956,   // w3:  initial stability for Easy
        6.4133,   // w4:  initial difficulty mean
        0.8334,   // w5:  initial difficulty modifier
        3.0194,   // w6:  stability increase base (ln)
        0.001,    // w7:  stability increase grade modifier
        1.8722,   // w8:  stability after lapse multiplier
        0.1666,   // w9:  hard penalty
        0.796,    // w10: easy bonus
        1.4835,   // w11: difficulty after success
        0.0614,   // w12: difficulty after failure
        0.2629,   // w13: difficulty constraint (mean reversion)
        1.6483,   // w14: short-term stability factor
        0.6014,   // w15: long-term stability factor
        1.8729,   // w16: stability growth rate
        0.5425,   // w17: difficulty growth rate
        0.0912,   // w18: stability decay on lapse
        0.0658,   // w19: difficulty decay on lapse
        0.1542,   // w20: power-law decay exponent
    ];

    /// <summary>
    /// Power-law forgetting curve.
    /// R(t) = (1 + factor * t / S) ^ (-w20)
    /// where factor = 0.9^(-1/w20) - 1
    /// </summary>
    /// <param name="daysElapsed">Time since last review, in days.</param>
    /// <param name="stability">Current stability value (in days).</param>
    /// <returns>Retrievability in [0, 1].</returns>
    public static double Retrievability(double daysElapsed, double stability)
    {
        if (stability <= 0)
            return 0.0;
        if (daysElapsed <= 0)
            return 1.0;

        var factor = Math.Pow(0.9, -1.0 / W[20]) - 1.0;
        return Math.Pow(1.0 + factor * daysElapsed / stability, -W[20]);
    }

    /// <summary>
    /// Composite memory score for search ranking.
    /// score = 0.7 * R + 0.3 * storageStrength, scaled by importanceWeight.
    /// </summary>
    /// <param name="retrievability">Current retrievability [0, 1].</param>
    /// <param name="storageStrength">Storage strength [0, 1].</param>
    /// <param name="importanceWeight">Importance multiplier (default 1.0).</param>
    /// <returns>Composite score.</returns>
    public static double MemoryScore(double retrievability, double storageStrength, double importanceWeight = 1.0)
    {
        var raw = 0.7 * retrievability + 0.3 * storageStrength;
        return raw * importanceWeight;
    }

    /// <summary>
    /// Infer a review grade from a signal type string.
    /// </summary>
    /// <param name="signalType">The signal type from the access log.</param>
    /// <returns>One of Again (1), Hard (2), Good (3), Easy (4).</returns>
    public static int InferGrade(string signalType)
    {
        return signalType?.ToLowerInvariant() switch
        {
            "used_in_response" => Good,
            "mentioned_by_user" => Easy,
            "user_correction" => Again,
            "contradiction" => Again,
            "reinforced" => Easy,
            "retrieved_not_used" => Hard,
            "search_hit" => Hard,
            "decayed" => Again,
            _ => Good,
        };
    }
}
