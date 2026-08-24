using System;
using System.Collections.Generic;

namespace Filters
{
    /// <summary>Lightweight state estimators for tracking and sensor fusion.</summary>
    public static class StateEstimationFilters
    {
        public static double[] ScalarKalman(IReadOnlyList<double> data, double measurementNoise = 4.0, double processNoise = 0.05, double initialVariance = 1.0)
        {
            double[] input = FilterMath.Copy(data);
            if (measurementNoise <= 0 || processNoise < 0 || initialVariance <= 0)
                throw new ArgumentOutOfRangeException("Noise and variance parameters are invalid.");
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double estimate = input[0];
            double variance = initialVariance;
            result[0] = estimate;
            for (int i = 1; i < input.Length; i++)
            {
                variance += processNoise;
                double gain = variance / (variance + measurementNoise);
                estimate += gain * (input[i] - estimate);
                variance *= 1 - gain;
                result[i] = estimate;
            }
            return result;
        }

        public static double[] AlphaBeta(IReadOnlyList<double> data, double samplePeriod, double alpha = 0.85, double beta = 0.005)
        {
            double[] input = FilterMath.Copy(data);
            if (samplePeriod <= 0) throw new ArgumentOutOfRangeException(nameof(samplePeriod));
            if (alpha < 0 || alpha > 1 || beta < 0 || beta > 1) throw new ArgumentOutOfRangeException("Alpha and beta must be in [0, 1].");
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double position = input[0];
            double velocity = 0;
            result[0] = position;
            for (int i = 1; i < input.Length; i++)
            {
                double predicted = position + velocity * samplePeriod;
                double residual = input[i] - predicted;
                position = predicted + alpha * residual;
                velocity += beta * residual / samplePeriod;
                result[i] = position;
            }
            return result;
        }
    }
}
