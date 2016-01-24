﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace principia {
namespace ksp_plugin_adapter {

class BurnEditor {
  public BurnEditor(WindowRenderer.ManagerInterface manager,
                    IntPtr plugin,
                    Vessel vessel,
                    double initial_time) {
    Δv_tangent_ = new DifferentialSlider(label            : "Δv tangent",
                                         unit             : "m / s",
                                         log10_lower_rate : Log10ΔvLowerRate,
                                         log10_upper_rate : Log10ΔvUpperRate);
    Δv_normal_ = new DifferentialSlider(label            : "Δv normal",
                                        unit             : "m / s",
                                        log10_lower_rate : Log10ΔvLowerRate,
                                        log10_upper_rate : Log10ΔvUpperRate);
    Δv_binormal_ = new DifferentialSlider(label            : "Δv binormal",
                                          unit             : "m / s",
                                          log10_lower_rate : Log10ΔvLowerRate,
                                          log10_upper_rate : Log10ΔvUpperRate);
    initial_time_ =
        new DifferentialSlider(
                label            : "t initial",
                unit             : null,
                log10_lower_rate : Log10TimeLowerRate,
                log10_upper_rate : Log10TimeUpperRate,
                min_value        : 0,
                max_value        : double.PositiveInfinity,
                formatter        : value =>
                    FormatTimeSpan(
                        TimeSpan.FromSeconds(
                            Planetarium.GetUniversalTime() - value)));
    reference_frame_selector_ = new ReferenceFrameSelector(
                                    manager,
                                    plugin,
                                    ReferenceFrameChanged,
                                    "Manœuvring frame");
    plugin_ = plugin;
    vessel_ = vessel;
    ComputeEngineCharacterestics();
  }

  // Renders the |BurnEditor|.  Returns true if and only if the settings were
  // changed.
  public bool Render(bool enabled) {
    var old_skin = UnityEngine.GUI.skin;
    UnityEngine.GUI.skin = null;
    UnityEngine.GUILayout.BeginVertical();
    bool changed = false;
    if (enabled) {
      reference_frame_selector_.RenderButton();
      UnityEngine.GUILayout.BeginHorizontal();
      if (UnityEngine.GUILayout.Button("Active Engines")) {
        engine_warning_ = "";
        ComputeEngineCharacterestics();
        changed = true;
      } else if (UnityEngine.GUILayout.Button("Active RCS")) {
        engine_warning_ = "";
        ComputeRCSCharacterestics();
        changed = true;
      } else if (UnityEngine.GUILayout.Button("Instant Impulse")) {
        engine_warning_ = "";
        UseMagicThrust();
        changed = true;
      }
      UnityEngine.GUILayout.EndHorizontal();
      UnityEngine.GUILayout.TextArea(engine_warning_);
    } else {
      reference_frame_selector_.Hide();
    }
    changed |= Δv_tangent_.Render(enabled);
    changed |= Δv_normal_.Render(enabled);
    changed |= Δv_binormal_.Render(enabled);
    changed |= initial_time_.Render(enabled);
    changed |= changed_reference_frame_;
    changed_reference_frame_ = false;
    UnityEngine.GUILayout.Label(
        text       : "(from end of previous burn)",
        options    : UnityEngine.GUILayout.Width(250));
    UnityEngine.GUILayout.EndVertical();
    UnityEngine.GUI.skin = old_skin;
    return changed && enabled;
  }

  public void Reset(Burn burn) {
    Δv_tangent_.value = burn.delta_v.x;
    Δv_normal_.value = burn.delta_v.y;
    Δv_binormal_.value = burn.delta_v.z;
    initial_time_.value = burn.initial_time;
    reference_frame_selector_.Reset(burn.frame);
  }

  public Burn Burn() {
    return new Burn{
        thrust_in_kilonewtons = thrust_in_kilonewtons_,
        specific_impulse_in_seconds_g0 = specific_impulse_in_seconds_g0_,
        frame = reference_frame_selector_.FrameParameters(),
        initial_time = initial_time_.value,
        delta_v = new XYZ{x = Δv_tangent_.value,
                          y = Δv_normal_.value,
                          z = Δv_binormal_.value } };
  }

  public void ReferenceFrameChanged() {
    changed_reference_frame_ = true;
  }

  public void Close() {
    reference_frame_selector_.Dispose();
  }

  private void ComputeEngineCharacterestics() {
    ModuleEngines[] active_engines =
        (from part in vessel_.parts
         select (from PartModule module in part.Modules
                 where module is ModuleEngines &&
                       (module as ModuleEngines).EngineIgnited
                 select module as ModuleEngines)).SelectMany(x => x).ToArray();
    Vector3d reference_direction = vessel_.ReferenceTransform.up;
    double[] thrusts =
        (from engine in active_engines
         select engine.maxThrust *
             (from transform in engine.thrustTransforms
              select Math.Max(0,
                              Vector3d.Dot(reference_direction,
                                           -transform.forward))).Average()).
            ToArray();
    thrust_in_kilonewtons_ = thrusts.Sum();

    // This would use zip if we had 4.0 or later.  We loop for now.
    double Σ_f_over_i_sp = 0;
    for (int i = 0; i < active_engines.Count(); ++i) {
      Σ_f_over_i_sp +=
          thrusts[i] / active_engines[i].atmosphereCurve.Evaluate(0);
    }
    specific_impulse_in_seconds_g0_ = thrust_in_kilonewtons_ / Σ_f_over_i_sp;

    // If there are no engines, fall back onto RCS.
    if (thrust_in_kilonewtons_ == 0) {
      engine_warning_ = "No active engines, falling back to RCS";
      ComputeRCSCharacterestics();
    }
  }

  private void ComputeRCSCharacterestics() {
    ModuleRCS[] active_rcs =
        (from part in vessel_.parts
         select (from PartModule module in part.Modules
                 where module is ModuleRCS &&
                       (module as ModuleRCS).rcsEnabled
                 select module as ModuleRCS)).SelectMany(x => x).ToArray();
    Vector3d reference_direction = vessel_.ReferenceTransform.up;
    List<double> thrusts = new List<double>();
    foreach (ModuleRCS rcs in active_rcs) {
      thrusts.Add(0);
      for (int i = 0; i < rcs.thrusterTransforms.Count; ++i) {
        thrusts[thrusts.Count - 1] +=
            Math.Max(0,
                     Vector3d.Dot(-rcs.thrusterTransforms[i].forward *
                                      rcs.thrustForces[i],
                                  reference_direction));
      }
    }
    thrust_in_kilonewtons_ = thrusts.Sum();

    // This would use zip if we had 4.0 or later.  We loop for now.
    double Σ_f_over_i_sp = 0;
    for (int i = 0; i < active_rcs.Count(); ++i) {
      Σ_f_over_i_sp +=
          thrusts[i] / active_rcs[i].atmosphereCurve.Evaluate(0);
    }
    specific_impulse_in_seconds_g0_ = thrust_in_kilonewtons_ / Σ_f_over_i_sp;

    // If RCS provides no thrust, model a virtually instant burn.
    if (thrust_in_kilonewtons_ == 0) {
      engine_warning_ = "No active RCS, modeling as instant burn";
      UseMagicThrust();
    }
  }

  private void UseMagicThrust() {
    thrust_in_kilonewtons_ = 1E15;
    specific_impulse_in_seconds_g0_ = 3E7;
  }

  // Returns the equivalent of the .NET >= 4 format
  // span.ToString(@"ddd \d hh \h mm \m\i\n ss.FFF \s").
  private string FormatTimeSpan (TimeSpan span) {
     return span.Days.ToString("000") + " d " +
            span.Hours.ToString("00") + " h " +
            span.Minutes.ToString("00") + " min " +
            span.Seconds.ToString("00") + " s";
  }

  private DifferentialSlider Δv_tangent_;
  private DifferentialSlider Δv_normal_;
  private DifferentialSlider Δv_binormal_;
  private DifferentialSlider initial_time_;
  private ReferenceFrameSelector reference_frame_selector_;
  private double thrust_in_kilonewtons_;
  private double specific_impulse_in_seconds_g0_;

  private const double Log10ΔvLowerRate = -3.0;
  private const double Log10ΔvUpperRate = 3.5;
  private const double Log10TimeLowerRate = 0.0;
  private const double Log10TimeUpperRate = 7.0;

  // Not owned.
  private readonly IntPtr plugin_;
  private readonly Vessel vessel_;

  private bool changed_reference_frame_ = false;
  private string engine_warning_ = "";
}

}  // namespace ksp_plugin_adapter
}  // namespace principia
