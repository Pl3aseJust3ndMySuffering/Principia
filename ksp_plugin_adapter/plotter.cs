﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace principia {
namespace ksp_plugin_adapter {

class Plotter {
  public Plotter(PrincipiaPluginAdapter adapter) {
    adapter_ = adapter;
  }

  public void Plot(DisposablePlanetarium planetarium,
                   string main_vessel_guid,
                   double history_length) {
    PlotCelestialTrajectories(planetarium, main_vessel_guid, history_length);
    if (main_vessel_guid == null) {
      return;
    }
    PlotVesselTrajectories(planetarium, main_vessel_guid, history_length);
  }

  private void PlotVesselTrajectories(DisposablePlanetarium planetarium,
                                      string main_vessel_guid,
                                      double history_length) {
    // Main vessel psychohistory and prediction.
    using (DisposableIterator rp2_lines_iterator =
        planetarium.PlanetariumPlotPsychohistory(
            Plugin,
            main_vessel_guid,
            history_length)) {
      GLLines.PlotRP2Lines(rp2_lines_iterator,
                           adapter_.history_colour,
                           adapter_.history_style);
    }
    using (DisposableIterator rp2_lines_iterator =
        planetarium.PlanetariumPlotPrediction(
            Plugin,
            main_vessel_guid)) {
      GLLines.PlotRP2Lines(rp2_lines_iterator,
                           adapter_.prediction_colour,
                           adapter_.prediction_style);
    }
    // Target psychohistory and prediction.
    string target_id = FlightGlobals.fetch.VesselTarget?.GetVessel()?.id.
        ToString();
    if (FlightGlobals.ActiveVessel != null &&
        !adapter_.plotting_frame_selector_.target_frame_selected &&
        target_id != null &&
        Plugin.HasVessel(target_id)) {
      fixed (UnityEngine.Vector3* vertices_data = vertices_) {
          planetarium.PlanetariumGetPsychohistoryVertices(
              Plugin,
              target_id,
              history_length,
              vertices_,)) {
        GLLines.PlotRP2Lines(rp2_lines_iterator,
                             adapter_.target_history_colour,
                             adapter_.target_history_style);
      }// TODO COMMENT ALL PAST CELESTIAL PLOTTING, ADD MESH PATH
      using (DisposableIterator rp2_lines_iterator =
          planetarium.PlanetariumPlotPrediction(
              Plugin,
              target_id)) {
        GLLines.PlotRP2Lines(rp2_lines_iterator,
                             adapter_.target_prediction_colour,
                             adapter_.target_prediction_style);
      }
    }
    // Main vessel flight plan.
    if (Plugin.FlightPlanExists(main_vessel_guid)) {
      int number_of_anomalous_manœuvres =
          Plugin.FlightPlanNumberOfAnomalousManoeuvres(main_vessel_guid);
      int number_of_manœuvres =
          Plugin.FlightPlanNumberOfManoeuvres(main_vessel_guid);
      int number_of_segments =
          Plugin.FlightPlanNumberOfSegments(main_vessel_guid);
      for (int i = 0; i < number_of_segments; ++i) {
        bool is_burn = i % 2 == 1;
        using (DisposableIterator rp2_lines_iterator =
            planetarium.PlanetariumPlotFlightPlanSegment(
                Plugin,
                main_vessel_guid,
                i)) {
          GLLines.PlotRP2Lines(rp2_lines_iterator,
                                is_burn
                                    ? adapter_.burn_colour
                                    : adapter_.flight_plan_colour,
                                is_burn
                                    ? adapter_.burn_style
                                    : adapter_.flight_plan_style);
        }
        if (is_burn) {
          int manœuvre_index = i / 2;
          if (manœuvre_index <
              number_of_manœuvres - number_of_anomalous_manœuvres) {
            NavigationManoeuvreFrenetTrihedron manœuvre =
                Plugin.FlightPlanGetManoeuvreFrenetTrihedron(
                    main_vessel_guid,
                    manœuvre_index);
          }
        }
      }
    }
  }

  private void PlotCelestialTrajectories(DisposablePlanetarium planetarium,
                                         string main_vessel_guid,
                                         double history_length) {
    const double degree = Math.PI / 180;
    UnityEngine.Camera camera = PlanetariumCamera.Camera;
    float vertical_fov = camera.fieldOfView;
    float horizontal_fov =
        UnityEngine.Camera.VerticalToHorizontalFieldOfView(
            vertical_fov, camera.aspect);
    // The angle subtended by the pixel closest to the centre of the viewport.
    double tan_angular_resolution = Math.Min(
            Math.Tan(vertical_fov * degree / 2) / (camera.pixelHeight / 2),
            Math.Tan(horizontal_fov * degree / 2) / (camera.pixelWidth / 2));
    PlotSubtreeTrajectories(planetarium, main_vessel_guid, history_length,
                            Planetarium.fetch.Sun, tan_angular_resolution);
  }

  private unsafe void PlotSubtreeTrajectories(DisposablePlanetarium planetarium,
                                              string main_vessel_guid,
                                              double history_length,
                                              CelestialBody root,
                                              double tan_angular_resolution) {
    CelestialTrajectories trajectories;
    if (!celestial_trajectories_.TryGetValue(root, out trajectories)) {
      trajectories = celestial_trajectories_[root] =
          new CelestialTrajectories();
    }
    var colour = root.orbitDriver?.Renderer?.orbitColor ??
        XKCDColors.SunshineYellow;
    var camera_world_position = ScaledSpace.ScaledToLocalSpace(
        PlanetariumCamera.fetch.transform.position);
    double min_distance_from_camera =
        (root.position - camera_world_position).magnitude;
    if (!adapter_.plotting_frame_selector_.FixesBody(root)) {
      UnityEngine.Mesh past_mesh = MainWindow.reuse_meshes ? trajectories.past : new UnityEngine.Mesh();
      fixed (UnityEngine.Vector3* vertices_data = vertices_) {
        planetarium.PlanetariumGetCelestialPastTrajectoryVertices(
            Plugin,
            root.flightGlobalsIndex,
            history_length,
            (IntPtr)vertices_data,
            vertices_.Length,
            out double min_past_distance,
            out int vertex_count);
        min_distance_from_camera =
            Math.Min(min_distance_from_camera, min_past_distance);
        DrawLineMesh(past_mesh, vertices_, vertex_count, colour,
                     GLLines.Style.Faded);
      }
      if (main_vessel_guid != null) {
        UnityEngine.Mesh future_mesh = MainWindow.reuse_meshes ? trajectories.future : new UnityEngine.Mesh();
        fixed (UnityEngine.Vector3* vertices_data = vertices_) {
          planetarium.PlanetariumGetCelestialFutureTrajectoryVertices(
              Plugin,
              root.flightGlobalsIndex,
              main_vessel_guid,
              (IntPtr)vertices_data,
              vertices_.Length,
              out double min_future_distance,
              out int vertex_count);
          DrawLineMesh(future_mesh, vertices_, vertex_count, colour,
                       GLLines.Style.Solid);
        }
      }
    }
    foreach (CelestialBody child in root.orbitingBodies) {
      // Plot the trajectory of an orbiting body if it could be separated from
      // that of its parent by a pixel of empty space, instead of merely making
      // the line wider.
      if (child.orbit.ApR / min_distance_from_camera >
              2 * tan_angular_resolution) {
        PlotSubtreeTrajectories(planetarium, main_vessel_guid, history_length,
                                child, tan_angular_resolution);
      }
    }
  }

  private static void DrawLineMesh(UnityEngine.Mesh mesh,
                                   UnityEngine.Vector3[] vertices,
                                   int vertex_count,
                                   UnityEngine.Color colour,
                                   GLLines.Style style) {
    mesh.vertices = vertices;
    var indices = new int[vertex_count];
    for (int i = 0; i < vertex_count; ++i) {
      indices[i] = i;
    }
    var colours = new UnityEngine.Color[vertices.Length];
    if (style == GLLines.Style.Faded) {
      for (int i = 0; i < colours.Length; ++i) {
        var faded_colour = colour;
        // Fade from the opacity of |colour| (when i = 0) down to 1/4 of that
        // opacity.
        faded_colour.a *= 1 - (float)(4 * i) / (float)(5 * colours.Length);
        colours[i] = faded_colour;
      }
    } else {
      for (int i = 0; i < colours.Length; ++i) {
        colours[i] = colour;
      }
    }
    mesh.colors = colours;
    mesh.SetIndices(
        indices,
        style == GLLines.Style.Dashed ? UnityEngine.MeshTopology.Lines
                                      : UnityEngine.MeshTopology.LineStrip,
        submesh: 0);
    if (MainWindow.reuse_meshes) {
      mesh.RecalculateBounds();
    }
    if (MainWindow.now) {
      UnityEngine.Graphics.DrawMeshNow(mesh, UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
    } else {
      UnityEngine.Graphics.DrawMesh(
          mesh,
          UnityEngine.Vector3.zero,
          UnityEngine.Quaternion.identity,
          GLLines.line_material,
          MainWindow.layer,
          PlanetariumCamera.Camera);
    }
  }

  private readonly PrincipiaPluginAdapter adapter_;

  private IntPtr Plugin => adapter_.Plugin();

  private UnityEngine.Vector3[] vertices_ = new UnityEngine.Vector3[10_000];

  private class CelestialTrajectories {
    public UnityEngine.Mesh future = new UnityEngine.Mesh();
    public UnityEngine.Mesh past = new UnityEngine.Mesh();
  }

  private Dictionary<CelestialBody, CelestialTrajectories> celestial_trajectories_ =
      new Dictionary<CelestialBody, CelestialTrajectories>();
}

}  // namespace ksp_plugin_adapter
}  // namespace principia
