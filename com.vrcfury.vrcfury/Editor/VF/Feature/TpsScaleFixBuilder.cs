using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VF.Builder;
using VF.Builder.Exceptions;
using VF.Builder.Haptics;
using VF.Feature.Base;
using VF.Inspector;
using VF.Model.Feature;

namespace VF.Feature {
    public class TpsScaleFixBuilder : FeatureBuilder<TpsScaleFix> {
        [FeatureBuilderAction(FeatureOrder.TpsScaleFix)]
        public void Apply() {
            var objectNumber = 0;
            BlendTree directTree = null;
            AnimationClip zeroClip = null;
            
            // Remove old fix attempts
            foreach (var clip in GetFx().GetClips()) {
                foreach (var binding in clip.GetFloatBindings()) {
                    if (binding.propertyName.Contains("_TPS_PenetratorLength") ||
                        binding.propertyName.Contains("_TPS_PenetratorScale")) {
                        clip.SetFloatCurve(binding, null);
                    }
                }
            }
            
            var animatedPaths = GetFx().GetClips()
                .SelectMany(clip => clip.GetFloatBindings())
                .Where(IsScaleBinding)
                .Select(b => b.path)
                .ToImmutableHashSet();
            
            foreach (var renderer in avatarObject.GetComponentsInChildren<Renderer>(true)) {
                var pathToRenderer =
                    AnimationUtility.CalculateTransformPath(renderer.transform, avatarObject.transform);
                if (renderer.sharedMaterials.Count(TpsConfigurer.IsTps) > 1) {
                    throw new VRCFBuilderException(
                        "TpsScaleFix cannot work if multiple TPS materials are used on a single renderer. "
                        + pathToRenderer);
                }

                renderer.sharedMaterials = renderer.sharedMaterials.Select(mat => {
                    Transform rootBone = renderer.transform;
                    if (renderer is SkinnedMeshRenderer skin && skin.rootBone != null) {
                        rootBone = skin.rootBone;
                    }
                    
                    if (!TpsConfigurer.IsTps(mat)) return mat;
                    if (TpsConfigurer.IsLocked(mat)) {
                        throw new VRCFBuilderException(
                            "TpsScaleFix requires that all TPS materials must be unlocked. " +
                            "Please unlock the material on " +
                            pathToRenderer);
                    }

                    var parentPaths =
                        rootBone.GetComponentsInParent<Transform>()
                            .Select(t => AnimationUtility.CalculateTransformPath(t, avatarObject.transform))
                            .ToList();

                    var animatedParentPaths = parentPaths
                        .Where(path => animatedPaths.Contains(path))
                        .ToList();

                    if (animatedParentPaths.Count == 0) return mat;

                    objectNumber++;
                    Debug.Log("Processing " + pathToRenderer + " " + mat);
                    mat = mutableManager.MakeMutable(mat);
                    mat.SetOverrideTag("_TPS_PenetratorLengthAnimated", "1");
                    mat.SetOverrideTag("_TPS_PenetratorScaleAnimated", "1");

                    var pathToParam = new Dictionary<string, VFAFloat>();
                    var pathNumber = 0;
                    foreach (var path in animatedParentPaths) {
                        pathNumber++;
                        var param = GetFx().NewFloat("tpsScale_" + objectNumber + "_" + pathNumber);
                        pathToParam[path] = param;
                        Debug.Log(path + " " + param.Name());
                    }

                    float handledScale = 1;
                    foreach (var path in animatedParentPaths) {
                        handledScale *= avatarObject.transform.Find(path).localScale.z;
                    }

                    var scaleOffset = mat.GetVector(TpsConfigurer.TpsPenetratorScale).z / handledScale;
                    var lengthOffset = mat.GetFloat(TpsConfigurer.TpsPenetratorLength) / handledScale;

                    var scaleClip = GetFx().NewClip("tpsScale_" + objectNumber);
                    scaleClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorScale.x", ClipBuilder.OneFrame(scaleOffset));
                    scaleClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorScale.y", ClipBuilder.OneFrame(scaleOffset));
                    scaleClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorScale.z", ClipBuilder.OneFrame(scaleOffset));
                    scaleClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorLength", ClipBuilder.OneFrame(lengthOffset));

                    if (directTree == null) {
                        Debug.Log("Creating direct layer");
                        var layer = GetFx().NewLayer("tpsScale");
                        var state = layer.NewState("Scale");
                        directTree = GetFx().NewBlendTree("tpsScale");
                        directTree.blendType = BlendTreeType.Direct;
                        state.WithAnimation(directTree);

                        zeroClip = GetFx().NewClip("zeroScale");
                        var one = GetFx().NewFloat("one", def: 1);
                        directTree.AddChild(zeroClip);
                        SetLastParam(directTree, one);
                    }
                    
                    zeroClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorScale.x", ClipBuilder.OneFrame(0));
                    zeroClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorScale.y", ClipBuilder.OneFrame(0));
                    zeroClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorScale.z", ClipBuilder.OneFrame(0));
                    zeroClip.SetCurve(pathToRenderer, renderer.GetType(), "material._TPS_PenetratorLength", ClipBuilder.OneFrame(0));

                    var tree = directTree;
                    foreach (var (param,index) in pathToParam.Values.Select((p,index) => (p,index))) {
                        var isLast = index == pathToParam.Count - 1;
                        if (isLast) {
                            tree.AddChild(scaleClip);
                            SetLastParam(tree, param);
                        } else {
                            var subTree = GetFx().NewBlendTree("tpsScaleSub");
                            subTree.blendType = BlendTreeType.Direct;
                            tree.AddChild(subTree);
                            SetLastParam(tree, param);
                            tree = subTree;
                        }
                    }

                    foreach (var clip in GetFx().GetClips()) {
                        foreach (var binding in clip.GetFloatBindings()) {
                            if (!IsScaleBinding(binding)) continue;
                            if (!pathToParam.TryGetValue(binding.path, out var param)) continue;
                            var newBinding = new EditorCurveBinding();
                            newBinding.type = typeof(Animator);
                            newBinding.path = "";
                            newBinding.propertyName = param.Name();
                            clip.SetFloatCurve(newBinding, clip.GetFloatCurve(binding));
                        }
                    }

                    return mat;
                }).ToArray();

            }
        }

        private static bool IsScaleBinding(EditorCurveBinding binding) {
            return binding.type == typeof(Transform) && binding.propertyName == "m_LocalScale.z";
        }

        private static void SetLastParam(BlendTree tree, VFAParam param) {
            var children = tree.children;
            var child = children[children.Length - 1];
            child.directBlendParameter = param.Name();
            children[children.Length - 1] = child;
            tree.children = children;
        }

        public override string GetEditorTitle() {
            return "TPS Scale Fix (BETA)";
        }

        public override VisualElement CreateEditor(SerializedProperty prop) {
            return VRCFuryEditorUtils.Info(
                "This feature will allow Poiyomi TPS to work properly with scaling. While active, avatar scaling, " +
                "object scaling, or any combination of the two may be used in conjunction with TPS.\n\n" +
                "Beware: this feature is BETA and may not work properly.");
        }

        public override bool AvailableOnProps() {
            return false;
        }
    }
}
