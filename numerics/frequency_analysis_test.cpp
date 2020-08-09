﻿
#include "numerics/frequency_analysis.hpp"

#include <random>
#include <vector>

#include "geometry/named_quantities.hpp"
#include "gmock/gmock.h"
#include "gtest/gtest.h"
#include "numerics/apodization.hpp"
#include "numerics/fast_fourier_transform.hpp"
#include "numerics/polynomial_evaluators.hpp"
#include "quantities/elementary_functions.hpp"
#include "quantities/named_quantities.hpp"
#include "quantities/quantities.hpp"
#include "quantities/si.hpp"
#include "testing_utilities/approximate_quantity.hpp"
#include "testing_utilities/is_near.hpp"
#include "testing_utilities/numerics_matchers.hpp"

namespace principia {
namespace numerics {
namespace frequency_analysis {

using geometry::Instant;
using quantities::AngularFrequency;
using quantities::Length;
using quantities::Sin;
using quantities::Square;
using quantities::Time;
using quantities::si::Metre;
using quantities::si::Radian;
using quantities::si::Second;
using testing_utilities::IsNear;
using testing_utilities::RelativeErrorFrom;
using testing_utilities::operator""_⑴;
namespace si = quantities::si;

class FrequencyAnalysisTest : public ::testing::Test {};

TEST_F(FrequencyAnalysisTest, PreciseMode) {
  using FFT = FastFourierTransform<Length, 1 << 16>;
  Instant const t0;
  AngularFrequency const ω = 666.543 * π / FFT::size * Radian / Second;
  Time const Δt = 1 * Second;
  std::mt19937_64 random(42);
  std::uniform_real_distribution<> amplitude_noise(-0.1, 0.1);
  std::uniform_real_distribution<> frequency_noise(-100.0, 100.0);

  using Series = PoissonSeries<Length, 0, HornerEvaluator>;
  Series::PolynomialsByAngularFrequency polynomials;

  // Main harmonic.
  polynomials.emplace(
      ω,
      Series::Polynomials{/*sin=*/Series::Polynomial({1 * Metre}, t0),
                          /*cos=*/Series::Polynomial({0 * Metre}, t0)});

  // Noise with lower amplitude and higher frequency.
  for (int i = 0; i < 10; ++i) {
    auto const sin_amplitude = amplitude_noise(random) * Metre;
    auto const cos_amplitude = amplitude_noise(random) * Metre;
    polynomials.emplace(
        ω * frequency_noise(random),
        Series::Polynomials{/*sin=*/Series::Polynomial({sin_amplitude}, t0),
                            /*cos=*/Series::Polynomial({cos_amplitude}, t0)});
  }
  Series const sin(Series::Polynomial({amplitude_noise(random) * Metre}, t0),
                    polynomials);

  Instant const t_min = t0;
  Instant const t_max = t0 + (FFT::size - 1) * Δt;
  std::vector<Length> signal;
  for (int n = 0; n < FFT::size; ++n) {
    signal.push_back(sin(t0 + n * Δt));
  }

  // Won't fit on the stack.
  auto transform = std::make_unique<FFT>(signal, Δt);

  // The FFT gives us an accuracy which is of the order of the number of points.
  auto const mode = transform->Mode();
  EXPECT_THAT(mode.midpoint(),
              RelativeErrorFrom(ω, IsNear(8.1e-4_⑴)));

  using Double = PoissonSeries<double, 0, HornerEvaluator>;

  std::function<Length(Series const& left,
                       Double const& right,
                       Double const& weight)> const dot =
    [t_min, t_max](Series const& left,
                   Double const& right,
                   Double const& weight) {
    return Dot(left, right, weight, t_min, t_max);
  };

  // The precise analysis is only limited by our ability to pinpoint the
  // maximum.
  auto const precise_mode = PreciseMode(
      mode, sin, apodization::Hann<HornerEvaluator>(t_min, t_max), dot);
  EXPECT_THAT(precise_mode,
              RelativeErrorFrom(ω, IsNear(6.4e-11_⑴)));
}

TEST_F(FrequencyAnalysisTest, Projection) {
  using Degree4 = PoissonSeries<Length, 4, HornerEvaluator>;
  auto const sin =
      internal_frequency_analysis::SeriesGenerator<Degree4, 2>::Sin(
          si::Unit<AngularFrequency>, Instant());
  auto const cos =
      internal_frequency_analysis::SeriesGenerator<Degree4, 2>::Cos(
          si::Unit<AngularFrequency>, Instant());
  auto const basis =
      internal_frequency_analysis::BasisGenerator<Degree4>::Basis(
          si::Unit<AngularFrequency>, Instant());
  LOG(ERROR)<<sin;
  LOG(ERROR)<<cos;
  for (int i = 0; i < basis.size(); ++i) {
    LOG(ERROR)<<i<<" ---- "<<basis[i];
  }
}

}  // namespace frequency_analysis
}  // namespace numerics
}  // namespace principia
