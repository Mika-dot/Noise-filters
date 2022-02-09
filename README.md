# Noise-filters
Algorithms for filtering input data from noise and outliers.

---
Realizable algorithms.

- [x] [Average](https://ru.wikipedia.org/wiki/%D0%A1%D1%80%D0%B5%D0%B4%D0%BD%D0%B5%D0%B5_%D0%B0%D1%80%D0%B8%D1%84%D0%BC%D0%B5%D1%82%D0%B8%D1%87%D0%B5%D1%81%D0%BA%D0%BE%D0%B5)
- [x] [Stretched selection](https://alexgyver.ru/lessons/filters/)
- [x] [running average](https://prog-cpp.ru/moving-average/)
- [x] [Exponential running average](https://ru.wikipedia.org/wiki/%D0%A1%D0%BA%D0%BE%D0%BB%D1%8C%D0%B7%D1%8F%D1%89%D0%B0%D1%8F_%D1%81%D1%80%D0%B5%D0%B4%D0%BD%D1%8F%D1%8F)
- [x] [Adaptive factor](https://ru.wikipedia.org/wiki/%D0%90%D0%B4%D0%B0%D0%BF%D1%82%D0%B8%D0%B2%D0%BD%D1%8B%D0%B9_%D1%84%D0%B8%D0%BB%D1%8C%D1%82%D1%80)
- [x] [Median filter](https://ru.wikipedia.org/wiki/%D0%9C%D0%B5%D0%B4%D0%B8%D0%B0%D0%BD%D0%BD%D1%8B%D0%B9_%D1%84%D0%B8%D0%BB%D1%8C%D1%82%D1%80)
- [x] [Least square method](https://ru.wikipedia.org/wiki/%D0%9C%D0%B5%D1%82%D0%BE%D0%B4_%D0%BD%D0%B0%D0%B8%D0%BC%D0%B5%D0%BD%D1%8C%D1%88%D0%B8%D1%85_%D0%BA%D0%B2%D0%B0%D0%B4%D1%80%D0%B0%D1%82%D0%BE%D0%B2)
- [x] [Simple "Kalman"](https://habr.com/ru/post/166693/)
- [x] [Alpha Beta Filter](https://hrwiki.ru/wiki/Alpha_beta_filter)
- [x] [Least square method](https://ru.wikipedia.org/wiki/%D0%9C%D0%B5%D1%82%D0%BE%D0%B4_%D0%BD%D0%B0%D0%B8%D0%BC%D0%B5%D0%BD%D1%8C%D1%88%D0%B8%D1%85_%D0%BA%D0%B2%D0%B0%D0%B4%D1%80%D0%B0%D1%82%D0%BE%D0%B2)
- [x] [Values]()
---


# Average

The arithmetic mean (in mathematics and statistics) is a kind of mean. It is defined as a number equal to the sum of all the numbers in the set divided by their number. It is one of the most common measures of central tendency.

It was proposed (along with the geometric mean and the harmonic mean) by the Pythagoreans [1].

Special cases of the arithmetic mean are the mean (of the general population) and the sample mean (of samples).

In the event that the number of elements of the set of numbers of a stationary random process is infinite, the mathematical expectation of a random variable plays the role of the arithmetic mean.

![Average](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/Average.PNG)

# Stretched selection

It differs from the previous one in that it sums up several measurements, and only after that gives the result. Between calculations gives the previous result:

![StretchedSelection](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/StretchedSelection.PNG)

# running average

This algorithm works on the principle of a buffer in which the last few measurements are stored for averaging. Each time the filter is called, the buffer is shifted, a new value is added to it and the oldest value is removed, then the buffer is averaged over the arithmetic mean.

![StretchedSelection](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/RunningAverage.PNG)

# Exponential running average

Moving average, moving average (MA) is the general name for a family of functions whose values at each definition point are equal to some average value of the original function over the previous period.

Moving averages are commonly used with time series data to smooth out short-term fluctuations and highlight major trends or cycles[1][2].

Mathematically, moving average is a type of convolution.

![ExponentialRunningAverage](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/ExponentialRunningAverage.PNG)

# Adaptive factor

An adaptive filter is a system with a linear filter[en] having a transfer function controlled by variable parameters and means for setting these parameters according to an optimization algorithm. Due to the complexity of optimization algorithms, almost all adaptive filters are digital filters. Adaptive filters are required for some applications because some parameters of the desired processing operation (for example, the location of reflective surfaces in the reverberant space) are not known in advance or change. The adaptive closed loop filter uses error feedback to optimize the transfer function.

Generally speaking, the closed-loop adaptive process involves applying a cost function, which is a criterion for optimal filter performance, to be used in an algorithm that determines how to modify the filter's transfer function to minimize cost at the next iteration. The most commonly used price function is the RMS value of the error signal.

As the power of digital signal processors has increased, adaptive filters have become more common and are now commonly used in devices such as mobile phones and other communication devices, camcorders and digital cameras, and medical monitoring equipment.

![AdaptiveFactor](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/AdaptiveFactor.PNG)


# Median filter

The median filter is a type of digital filter widely used in digital signal and image processing to reduce noise. The median filter is a non-linear FIR filter.

Sample values ​​inside the filter window are sorted in ascending (descending) order; and the value in the middle of the ordered list goes to the output of the filter. In the case of an even number of samples in the window, the output value of the filter is equal to the average of the two samples in the middle of the ordered list. The window moves along the filtered signal and the calculations are repeated.

Median filtering is an efficient procedure for processing signals affected by impulse noise.

![MedianFilter](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/MedianFilter.PNG)

# Simple "Kalman"

I found this algorithm on the Internet, I lost the source. In the filter, you can set the measurement spread (expected measurement noise), the estimate spread (it adjusts itself during the filter operation, you can set it to the same as the measurement spread), the rate of change of values (0.001-1, vary by yourself).

![SimpleKalman](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/SimpleKalman.PNG)

# Alpha Beta Filter

The alpha-beta filter (also called the alpha-beta filter, fg filter, or gh filter) is a simplified form of observer for estimation, data smoothing, and control applications. It is closely related to Kalman filters and linear state observers used in control theory. Its main advantage is that it does not require a detailed system model.

![AlphaBetaFilter](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/AlphaBetaFilter.PNG)

# Least square method

The least squares method (LSM) is a mathematical method used to solve various problems, based on minimizing the sum of squared deviations of some functions from the desired variables. It can be used to "solve" overdetermined systems of equations (when the number of equations exceeds the number of unknowns), to find a solution in the case of ordinary (not overdetermined) nonlinear systems of equations, to approximate the point values ​​of some function. OLS is one of the basic methods of regression analysis for estimating unknown parameters of regression models from sample data.

![LeastSquareMethod2](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/LeastSquareMethod2.PNG)

# Values

Simply reveals the amount of noise and emissions

![NoiseCalculation](https://github.com/Mika-dot/Noise-filters/blob/main/Filter%20in%20charts/NoiseCalculation.PNG)
