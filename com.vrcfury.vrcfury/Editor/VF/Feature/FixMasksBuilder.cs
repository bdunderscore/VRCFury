using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VF.Builder;
using VF.Feature.Base;
using VF.Injector;
using VF.Service;
using VF.Utils;
using VF.Utils.Controller;
using VRC.SDK3.Avatars.Components;

namespace VF.Feature {
    public class FixMasksBuilder : FeatureBuilder {
        [VFAutowired] private AnimatorLayerControlOffsetBuilder animatorLayerControlOffsetBuilder;
        [VFAutowired] private LayerSourceService layerSourceService;
        
        [FeatureBuilderAction(FeatureOrder.FixGestureFxConflict)]
        public void FixGestureFxConflict() {
            // Merge Gesture into FX
            var fx = manager.GetFx();
            var gesture = manager.GetController(VRCAvatarDescriptor.AnimLayerType.Gesture);
            fx.TakeOwnershipOf(gesture.GetRaw(), putOnTop: true, prefix: false);

            // Make Gesture an exact copy of FX
            var fxCopy = MutableManager.CopyRecursive((AnimatorController)fx.GetRaw(), false, (orig, copy) => {
                if (orig is AnimatorStateMachine origSm && copy is AnimatorStateMachine copySm) {
                    animatorLayerControlOffsetBuilder.NotifyOfCopiedStateMachine(origSm, copySm);
                    layerSourceService.CopySource(origSm, copySm);
                }
            });
            gesture.TakeOwnershipOf(fxCopy, prefix: false);

            var fxLayers = fx.GetLayers().ToArray();
            var gestureLayers = gesture.GetLayers().ToArray();
            if (fxLayers.Length != gestureLayers.Length) {
                throw new Exception("Gesture+FX copy didn't match in length");
            }

            var layersToKeep = new HashSet<AnimatorStateMachine>();
            var layersToKeepBehaviors = new HashSet<AnimatorStateMachine>();
            foreach (var (fxLayer,gestureLayer) in fxLayers.Zip(gestureLayers, (a,b) => (a,b))) {
                var typesForLayer = new AnimatorIterator.Clips().From(fxLayer)
                    .SelectMany(clip => clip.GetAllBindings())
                    .Select(GetLayerTypeForBinding)
                    .ToImmutableHashSet();

                if (typesForLayer.Contains(BindingType.Gesture) && typesForLayer.Contains(BindingType.FX)) {
                    layersToKeep.Add(fxLayer);
                    layersToKeep.Add(gestureLayer);
                    layersToKeepBehaviors.Add(fxLayer);
                } else if (typesForLayer.Contains(BindingType.Gesture)) {
                    layersToKeep.Add(gestureLayer);
                    layersToKeepBehaviors.Add(gestureLayer);
                } else {
                    layersToKeep.Add(fxLayer);
                    layersToKeepBehaviors.Add(fxLayer);
                }
            }

            KeepLayersDrivingUsedParams(fx.GetRaw(), layersToKeep);
            KeepLayersDrivingUsedParams(gesture.GetRaw(), layersToKeep);

            foreach (var layer in fx.GetLayers().Concat(gesture.GetLayers())) {
                if (!layersToKeep.Contains(layer)) {
                    layer.Remove();
                    continue;
                }
                if (!layersToKeepBehaviors.Contains(layer)) {
                    AnimatorIterator.ForEachBehaviourRW(
                        layer,
                        (b, add) => false
                    );
                }
            }

            foreach (var layer in fx.GetLayers()) {
                foreach (var clip in new AnimatorIterator.Clips().From(layer)) {
                    clip.Rewrite(AnimationRewriter.RewriteBinding(b => {
                        if (GetLayerTypeForBinding(b) == BindingType.Gesture) return null;
                        return b;
                    }, false));
                }
            }
            foreach (var layer in gesture.GetLayers()) {
                foreach (var clip in new AnimatorIterator.Clips().From(layer)) {
                    clip.Rewrite(AnimationRewriter.RewriteBinding(b => {
                        if (GetLayerTypeForBinding(b) == BindingType.FX) return null;
                        return b;
                    }, false));
                }
            }
        }

        enum BindingType {
            Gesture,
            FX,
            Animator
        }
        private static BindingType GetLayerTypeForBinding(EditorCurveBinding binding) {
            if (binding.IsMuscle() || binding.IsProxyBinding() || binding.type == typeof(Transform)) {
                return BindingType.Gesture;
            } else if (binding.path == "" && binding.type == typeof(Animator)) {
                return BindingType.Animator;
            } else {
                return BindingType.FX;
            }
        }

        private static void KeepLayersDrivingUsedParams(VFController ctrl, HashSet<AnimatorStateMachine> layersToKeep) {
            while (true) {
                var myKeptLayers = ctrl.GetLayers()
                    .Select(l => l.stateMachine)
                    .Where(layersToKeep.Contains)
                    .ToHashSet();
                var paramsUsed = new HashSet<string>();
                ((AnimatorController)ctrl).RewriteParameters(
                    str => { paramsUsed.Add(str); return str; },
                    false,
                    myKeptLayers
                );
                var layersControllingThoseParams = ctrl.GetLayers()
                    .Where(layer => LayerControlsOneOfParams(layer, paramsUsed))
                    .Select(l => l.stateMachine)
                    .ToImmutableHashSet();
                if (myKeptLayers.Intersect(layersControllingThoseParams).Count() == layersControllingThoseParams.Count) {
                    // Found no new layers to keep
                    break;
                }
                layersToKeep.UnionWith(layersControllingThoseParams);
            }
        }

        private static bool LayerControlsOneOfParams(VFLayer layer, ICollection<string> prms) {
            return new AnimatorIterator.Clips().From(layer)
                .SelectMany(clip => clip.GetFloatBindings())
                .Any(binding => binding.type == typeof(Animator)
                                && binding.path == ""
                                && prms.Contains(binding.propertyName));
        }

        [FeatureBuilderAction(FeatureOrder.FixMasks)]
        public void FixMasks() {
            // Remove redundant FX masks if they're not needed
            foreach (var layer in GetFx().GetLayers()) {
                if (layer.mask != null && layer.mask.AllowsAllTransforms()) {
                    layer.mask = null;
                }
            }

            GetFx().EnsureEmptyBaseLayer().mask = null;
            
            var gestureMask = AvatarMaskExtensions.Empty();
            gestureMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            gestureMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            gestureMask.AllowAllTransforms();
            manager.GetController(VRCAvatarDescriptor.AnimLayerType.Gesture).EnsureEmptyBaseLayer().mask = gestureMask;
        }
    }
}
