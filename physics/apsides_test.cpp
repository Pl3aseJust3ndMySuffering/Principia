#include "physics/apsides.hpp"

#include <limits>
#include <map>
#include <optional>
#include <vector>

#include "geometry/instant.hpp"
#include "geometry/space.hpp"
#include "gtest/gtest.h"
#include "gmock/gmock.h"
#include "integrators/embedded_explicit_runge_kutta_nyström_integrator.hpp"
#include "integrators/methods.hpp"
#include "integrators/symmetric_linear_multistep_integrator.hpp"
#include "physics/ephemeris.hpp"
#include "physics/kepler_orbit.hpp"
#include "quantities/astronomy.hpp"
#include "testing_utilities/almost_equals.hpp"
#include "testing_utilities/matchers.hpp"

namespace principia {
namespace physics {

using ::testing::Eq;
using ::testing::SizeIs;
using namespace principia::base::_not_null;
using namespace principia::geometry::_frame;
using namespace principia::geometry::_grassmann;
using namespace principia::geometry::_instant;
using namespace principia::geometry::_space;
using namespace principia::integrators::_embedded_explicit_runge_kutta_nyström_integrator;  // NOLINT
using namespace principia::integrators::_methods;
using namespace principia::integrators::_symmetric_linear_multistep_integrator;
using namespace principia::physics::_apsides;
using namespace principia::physics::_degrees_of_freedom;
using namespace principia::physics::_discrete_trajectory;
using namespace principia::physics::_ephemeris;
using namespace principia::physics::_kepler_orbit;
using namespace principia::physics::_massive_body;
using namespace principia::physics::_massless_body;
using namespace principia::quantities::_astronomy;
using namespace principia::quantities::_elementary_functions;
using namespace principia::quantities::_named_quantities;
using namespace principia::quantities::_quantities;
using namespace principia::quantities::_si;
using namespace principia::testing_utilities::_almost_equals;

class ApsidesTest : public ::testing::Test {
 protected:
  using World = Frame<struct WorldTag, Inertial>;
};

#if !defined(_DEBUG)

TEST_F(ApsidesTest, ComputeApsidesDiscreteTrajectory) {
  Instant const t0;
  GravitationalParameter const μ = SolarGravitationalParameter;
  auto const b = new MassiveBody(μ);

  std::vector<not_null<std::unique_ptr<MassiveBody const>>> bodies;
  std::vector<DegreesOfFreedom<World>> initial_state;
  bodies.emplace_back(std::unique_ptr<MassiveBody const>(b));
  initial_state.emplace_back(World::origin, World::unmoving);

  Ephemeris<World> ephemeris(
      std::move(bodies),
      initial_state,
      t0,
      /*accuracy_parameters=*/{/*fitting_tolerance=*/1 * Metre,
                               /*geopotential_tolerance=*/0x1p-24},
      Ephemeris<World>::FixedStepParameters(
          SymmetricLinearMultistepIntegrator<
              QuinlanTremaine1990Order12,
              Ephemeris<World>::NewtonianMotionEquation>(),
          10 * Minute));

  Displacement<World> r(
      {1 * AstronomicalUnit, 2 * AstronomicalUnit, 3 * AstronomicalUnit});
  Length const r_norm = r.Norm();
  Velocity<World> v({4 * Kilo(Metre) / Second,
                     5 * Kilo(Metre) / Second,
                     6 * Kilo(Metre) / Second});
  Speed const v_norm = v.Norm();

  Time const T = 2 * π * Sqrt(-(Pow<3>(r_norm) * Pow<2>(μ) /
                                Pow<3>(r_norm * Pow<2>(v_norm) - 2 * μ)));
  Length const a = -r_norm * μ / (r_norm * Pow<2>(v_norm) - 2 * μ);

  DiscreteTrajectory<World> trajectory;
  EXPECT_OK(trajectory.Append(t0,
                              DegreesOfFreedom<World>(World::origin + r, v)));

  EXPECT_OK(ephemeris.FlowWithAdaptiveStep(
      &trajectory,
      Ephemeris<World>::NoIntrinsicAcceleration,
      t0 + 10 * JulianYear,
      Ephemeris<World>::AdaptiveStepParameters(
          EmbeddedExplicitRungeKuttaNyströmIntegrator<
              DormandالمكاوىPrince1986RKN434FM,
              Ephemeris<World>::NewtonianMotionEquation>(),
          std::numeric_limits<std::int64_t>::max(),
          1e-3 * Metre,
          1e-3 * Metre / Second),
      Ephemeris<World>::unlimited_max_ephemeris_steps));

  DiscreteTrajectory<World> apoapsides;
  DiscreteTrajectory<World> periapsides;
  ComputeApsides(*ephemeris.trajectory(b),
                 trajectory,
                 trajectory.begin(),
                 trajectory.end(),
                 /*max_points=*/std::numeric_limits<int>::max(),
                 apoapsides,
                 periapsides);

  std::optional<Instant> previous_time;
  std::map<Instant, DegreesOfFreedom<World>> all_apsides;
  for (auto const& [time, degrees_of_freedom] : apoapsides) {
    all_apsides.emplace(time, degrees_of_freedom);
    if (previous_time) {
      EXPECT_THAT(time - *previous_time, AlmostEquals(T, 118, 2824));
    }
    previous_time = time;
  }

  previous_time = std::nullopt;
  for (auto const& [time, degrees_of_freedom] : periapsides) {
    all_apsides.emplace(time, degrees_of_freedom);
    if (previous_time) {
      EXPECT_THAT(time - *previous_time, AlmostEquals(T, 134, 257));
    }
    previous_time = time;
  }

  EXPECT_EQ(6, all_apsides.size());

  previous_time = std::nullopt;
  std::optional<Position<World>> previous_position;
  for (auto const& [time, degrees_of_freedom] : all_apsides) {
    Position<World> const position = degrees_of_freedom.position();
    if (previous_time) {
      EXPECT_THAT(time - *previous_time,
                  AlmostEquals(0.5 * T, 103, 5098));
      EXPECT_THAT((position - *previous_position).Norm(),
                  AlmostEquals(2.0 * a, 0, 176));
    }
    previous_time = time;
    previous_position = position;
  }
}

TEST_F(ApsidesTest, ComputeNodes) {
  Instant const t0;
  GravitationalParameter const μ = SolarGravitationalParameter;
  auto const b = new MassiveBody(μ);

  std::vector<not_null<std::unique_ptr<MassiveBody const>>> bodies;
  std::vector<DegreesOfFreedom<World>> initial_state;
  bodies.emplace_back(std::unique_ptr<MassiveBody const>(b));
  initial_state.emplace_back(World::origin, World::unmoving);

  Ephemeris<World> ephemeris(
      std::move(bodies),
      initial_state,
      t0,
      /*accuracy_parameters=*/{/*fitting_tolerance=*/1 * Metre,
                               /*geopotential_tolerance=*/0x1p-24},
      Ephemeris<World>::FixedStepParameters(
          SymmetricLinearMultistepIntegrator<
              QuinlanTremaine1990Order12,
              Ephemeris<World>::NewtonianMotionEquation>(),
          10 * Minute));

  KeplerianElements<World> elements;
  elements.eccentricity = 0.25;
  elements.semimajor_axis = 1 * AstronomicalUnit;
  elements.inclination = 10 * Degree;
  elements.longitude_of_ascending_node = 42 * Degree;
  elements.argument_of_periapsis = 100 * Degree;
  elements.mean_anomaly = 0 * Degree;
  KeplerOrbit<World> const orbit{
      *ephemeris.bodies()[0], MasslessBody{}, elements, t0};
  elements = orbit.elements_at_epoch();

  DiscreteTrajectory<World> trajectory;
  EXPECT_OK(trajectory.Append(t0, initial_state[0] + orbit.StateVectors(t0)));

  EXPECT_OK(ephemeris.FlowWithAdaptiveStep(
      &trajectory,
      Ephemeris<World>::NoIntrinsicAcceleration,
      t0 + 10 * JulianYear,
      Ephemeris<World>::AdaptiveStepParameters(
          EmbeddedExplicitRungeKuttaNyströmIntegrator<
              DormandالمكاوىPrince1986RKN434FM,
              Ephemeris<World>::NewtonianMotionEquation>(),
          std::numeric_limits<std::int64_t>::max(),
          1e-3 * Metre,
          1e-3 * Metre / Second),
      Ephemeris<World>::unlimited_max_ephemeris_steps));

  Vector<double, World> const north({0, 0, 1});

  DiscreteTrajectory<World> ascending_nodes;
  DiscreteTrajectory<World> descending_nodes;
  EXPECT_OK(ComputeNodes(trajectory,
                         trajectory.begin(),
                         trajectory.end(),
                         north,
                         /*max_points=*/std::numeric_limits<int>::max(),
                         ascending_nodes,
                         descending_nodes));

  std::optional<Instant> previous_time;
  for (auto const& [time, degrees_of_freedom] : ascending_nodes) {
    EXPECT_THAT((degrees_of_freedom.position() - World::origin)
                    .coordinates()
                    .ToSpherical()
                    .longitude,
                AlmostEquals(elements.longitude_of_ascending_node, 0, 104));
    if (previous_time) {
      EXPECT_THAT(time - *previous_time, AlmostEquals(*elements.period, 0, 20));
    }
    previous_time = time;
  }

  previous_time = std::nullopt;
  for (auto const& [time, degrees_of_freedom] : descending_nodes) {
    EXPECT_THAT(
        (degrees_of_freedom.position() - World::origin)
                .coordinates()
                .ToSpherical()
                .longitude,
        AlmostEquals(elements.longitude_of_ascending_node - π * Radian, 0, 29));
    if (previous_time) {
      EXPECT_THAT(time - *previous_time, AlmostEquals(*elements.period, 0, 29));
    }
    previous_time = time;
  }

  EXPECT_THAT(ascending_nodes, SizeIs(10));
  EXPECT_THAT(descending_nodes, SizeIs(10));

  DiscreteTrajectory<World> south_ascending_nodes;
  DiscreteTrajectory<World> south_descending_nodes;
  Vector<double, World> const mostly_south({1, 1, -1});
  EXPECT_OK(ComputeNodes(trajectory,
                         trajectory.begin(),
                         trajectory.end(),
                         mostly_south,
                         /*max_points=*/std::numeric_limits<int>::max(),
                         south_ascending_nodes,
                         south_descending_nodes));
  EXPECT_THAT(south_ascending_nodes, SizeIs(10));
  EXPECT_THAT(south_descending_nodes, SizeIs(10));

  for (auto south_ascending_it  = south_ascending_nodes.begin(),
            ascending_it        = ascending_nodes.begin(),
            south_descending_it = south_descending_nodes.begin(),
            descending_it       = descending_nodes.begin();
       south_ascending_it != south_ascending_nodes.end();
       ++south_ascending_it,
       ++ascending_it,
       ++south_descending_it,
       ++descending_it) {
    EXPECT_THAT(south_ascending_it->degrees_of_freedom,
                Eq(descending_it->degrees_of_freedom));
    EXPECT_THAT(south_ascending_it->time, Eq(descending_it->time));
    EXPECT_THAT(south_descending_it->degrees_of_freedom,
                Eq(ascending_it->degrees_of_freedom));
    EXPECT_THAT(south_descending_it->time, Eq(ascending_it->time));
  }
}

#endif

}  // namespace physics
}  // namespace principia
