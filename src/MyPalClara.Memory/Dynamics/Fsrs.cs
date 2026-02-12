namespace MyPalClara.Memory.Dynamics;

/// <summary>FSRS review grades.</summary>
public enum Grade { Again = 1, Hard = 2, Good = 3, Easy = 4 }

/// <summary>Current state of a memory item in the FSRS system.</summary>
public sealed record MemoryState(
    double Stability = 1.0,
    double Difficulty = 5.0,
    double RetrievalStrength = 1.0,
    double StorageStrength = 0.5,
    DateTime? LastReview = null,
    int ReviewCount = 0);

/// <summary>Result of applying a review to a memory state.</summary>
public sealed record ReviewResult(
    MemoryState NewState,
    double RetrievabilityBefore,
    double NextReviewDays);

/// <summary>
/// FSRS-6 spaced repetition algorithm. Direct port of clara_core/fsrs.py.
/// 21-weight power-law forgetting curve with Bjork dual-strength model.
/// </summary>
public static class Fsrs
{
    /// <summary>Default FSRS-6 parameters (21 weights).</summary>
    public static readonly double[] DefaultWeights =
    [
        0.212,   // w[0]:  Initial stability for Again
        1.2931,  // w[1]:  Initial stability for Hard
        2.3065,  // w[2]:  Initial stability for Good
        8.2956,  // w[3]:  Initial stability for Easy
        6.4133,  // w[4]:  Initial difficulty mean
        0.8334,  // w[5]:  Initial difficulty modifier
        3.0194,  // w[6]:  Stability increase base
        0.001,   // w[7]:  Stability increase grade modifier
        1.8722,  // w[8]:  Stability after lapse multiplier
        0.1666,  // w[9]:  Hard penalty
        0.796,   // w[10]: Easy bonus
        1.4835,  // w[11]: Difficulty after success
        0.0614,  // w[12]: Difficulty after failure
        0.2629,  // w[13]: Difficulty constraint (mean reversion)
        1.6483,  // w[14]: Short-term stability factor
        0.6014,  // w[15]: Long-term stability factor
        1.8729,  // w[16]: Stability growth rate
        0.5425,  // w[17]: Difficulty growth rate
        0.0912,  // w[18]: Stability decay on lapse
        0.0658,  // w[19]: Difficulty decay on lapse
        0.1542,  // w[20]: Power-law decay exponent (retrievability)
    ];

    /// <summary>
    /// Calculate probability of recall using FSRS-6 power-law decay.
    /// R(t) = (1 + t/S * factor)^(-w20)
    /// </summary>
    public static double Retrievability(double daysElapsed, double stability, double w20 = 0.1542)
    {
        if (daysElapsed <= 0) return 1.0;
        if (stability <= 0) return 0.0;

        // factor = 0.9^(-1/w20) - 1
        var factor = Math.Pow(0.9, -1.0 / w20) - 1.0;
        return Math.Pow(1.0 + factor * daysElapsed / stability, -w20);
    }

    /// <summary>Initial stability for a new memory: w[grade-1].</summary>
    public static double InitialStability(Grade grade, double[]? w = null)
    {
        w ??= DefaultWeights;
        return w[(int)grade - 1];
    }

    /// <summary>Initial difficulty: D0 = w[4] - exp(w[5] * (grade - 1)) + 1, clamped 1-10.</summary>
    public static double InitialDifficulty(Grade grade, double[]? w = null)
    {
        w ??= DefaultWeights;
        var d0 = w[4] - Math.Exp(w[5] * ((int)grade - 1)) + 1;
        return ConstrainDifficulty(d0);
    }

    /// <summary>Update difficulty after a review with mean reversion.</summary>
    public static double UpdateDifficulty(double currentDifficulty, Grade grade, double[]? w = null)
    {
        w ??= DefaultWeights;
        var delta = w[11] * ((int)grade - 3);
        var newD = MeanReversion(currentDifficulty + delta, w);
        return ConstrainDifficulty(newD);
    }

    /// <summary>
    /// Update stability after successful recall (grade >= Hard).
    /// S' = S * (1 + exp(w[6]) * (11-D) * S^(-w[7]) * (exp(w[8]*(1-R))-1) * bonus)
    /// </summary>
    public static double UpdateStabilitySuccess(
        double s, double d, double r, Grade grade, double[]? w = null)
    {
        w ??= DefaultWeights;

        var bonus = grade switch
        {
            Grade.Hard => w[9],   // Hard penalty (< 1)
            Grade.Easy => w[10],  // Easy bonus (> 1)
            _ => 1.0,
        };

        var stabilityFactor = Math.Exp(w[6]);
        var difficultyFactor = 11 - d;
        var stabilityDecay = Math.Pow(s, -w[7]);
        var retrievabilityFactor = Math.Exp(w[8] * (1 - r)) - 1;

        var growth = stabilityFactor * difficultyFactor * stabilityDecay * retrievabilityFactor * bonus;
        return Math.Max(0.1, s * (1 + growth));
    }

    /// <summary>
    /// Update stability after failed recall (grade = Again).
    /// S' = w[14] * D^(-w[15]) * ((S+1)^w[16]-1) * exp(w[17]*(1-R))
    /// </summary>
    public static double UpdateStabilityFailure(double s, double d, double r, double[]? w = null)
    {
        w ??= DefaultWeights;

        var difficultyFactor = Math.Pow(d, -w[15]);
        var stabilityFactor = Math.Pow(s + 1, w[16]) - 1;
        var retrievabilityFactor = Math.Exp(w[17] * (1 - r));

        var newS = w[14] * difficultyFactor * stabilityFactor * retrievabilityFactor;
        return Math.Max(0.1, Math.Min(newS, s)); // Don't increase on failure
    }

    /// <summary>
    /// Update Bjork dual-strength model (retrieval + storage strength).
    /// </summary>
    public static (double RetrievalStrength, double StorageStrength) UpdateDualStrength(
        double currentRetrieval, double currentStorage, Grade grade, double elapsedDays)
    {
        // Retrieval strength decays exponentially
        var decayRate = 0.1 * (1.0 / (1.0 + currentStorage));
        var decayedRetrieval = currentRetrieval * Math.Exp(-decayRate * elapsedDays);

        if (grade == Grade.Again)
        {
            return (0.3, currentStorage + 0.05);
        }

        // Desirable difficulty: lower retrieval = higher storage gain
        var difficultyBonus = Math.Max(0, 1 - decayedRetrieval);

        var (retrievalBoost, storageGain) = grade switch
        {
            Grade.Hard => (0.5, 0.1 + 0.1 * difficultyBonus),
            Grade.Good => (0.7, 0.15 + 0.15 * difficultyBonus),
            Grade.Easy => (0.9, 0.1 + 0.05 * difficultyBonus),
            _ => (0.7, 0.15),
        };

        var newRetrieval = Math.Min(1.0, decayedRetrieval + retrievalBoost);
        var newStorage = Math.Min(1.0, currentStorage + storageGain);

        return (newRetrieval, newStorage);
    }

    /// <summary>Main entry point: apply a review to a memory state.</summary>
    public static ReviewResult Review(MemoryState state, Grade grade, DateTime? reviewTime = null, double[]? w = null)
    {
        w ??= DefaultWeights;
        reviewTime ??= DateTime.UtcNow;

        var elapsedDays = state.LastReview.HasValue
            ? (reviewTime.Value - state.LastReview.Value).TotalDays
            : 0.0;

        var currentR = state.ReviewCount == 0
            ? 1.0
            : Retrievability(elapsedDays, state.Stability, w[20]);

        double newStability, newDifficulty;

        if (state.ReviewCount == 0)
        {
            newStability = InitialStability(grade, w);
            newDifficulty = InitialDifficulty(grade, w);
        }
        else
        {
            newDifficulty = UpdateDifficulty(state.Difficulty, grade, w);
            newStability = grade == Grade.Again
                ? UpdateStabilityFailure(state.Stability, state.Difficulty, currentR, w)
                : UpdateStabilitySuccess(state.Stability, state.Difficulty, currentR, grade, w);
        }

        var (newRetrieval, newStorage) = UpdateDualStrength(
            state.RetrievalStrength, state.StorageStrength, grade, elapsedDays);

        var newState = new MemoryState(
            Stability: newStability,
            Difficulty: newDifficulty,
            RetrievalStrength: newRetrieval,
            StorageStrength: newStorage,
            LastReview: reviewTime,
            ReviewCount: state.ReviewCount + 1);

        return new ReviewResult(newState, currentR, newStability);
    }

    /// <summary>Maps implicit signal types to Grade.</summary>
    public static Grade InferGrade(string signalType) => signalType switch
    {
        "used_in_response" => Grade.Good,
        "mentioned_by_user" => Grade.Easy,
        "user_correction" => Grade.Again,
        "task_completed" => Grade.Easy,
        "explicit_recall" => Grade.Good,
        "contradiction_detected" => Grade.Again,
        "implicit_reference" => Grade.Good,
        "partial_recall" => Grade.Hard,
        _ => Grade.Good,
    };

    /// <summary>Composite memory score for ranking: (0.7*R + 0.3*Rs) * importance.</summary>
    public static double CalculateMemoryScore(double retrievability, double storageStrength, double importanceWeight = 1.0)
        => (0.7 * retrievability + 0.3 * storageStrength) * importanceWeight;

    private static double ConstrainDifficulty(double d) => Math.Max(1.0, Math.Min(10.0, d));

    private static double MeanReversion(double d, double[] w) => w[13] * w[4] + (1 - w[13]) * d;
}
