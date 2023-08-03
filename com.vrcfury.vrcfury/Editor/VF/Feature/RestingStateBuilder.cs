using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VF.Feature.Base;
using VF.Inspector;
using VF.Utils;

namespace VF.Feature {
    /**
     * This builder is in charge of changing the resting state of the avatar for all the other builders.
     * If two builders make a conflicting decision, something is wrong (perhaps the user gave conflicting instructions?)
     */
    public class RestingStateBuilder : FeatureBuilder {

        private readonly List<AnimationClip> pendingClips = new List<AnimationClip>();

        public void ApplyClipToRestingState(AnimationClip clip, bool recordDefaultStateFirst = false) {
            if (recordDefaultStateFirst) {
                var defaultsManager = GetBuilder<FixWriteDefaultsBuilder>();
                foreach (var b in clip.GetFloatBindings())
                    defaultsManager.RecordDefaultNow(b, true);
                foreach (var b in clip.GetObjectBindings())
                    defaultsManager.RecordDefaultNow(b, false);
            }

            var copy = new AnimationClip();
            copy.CopyFrom(clip);
            pendingClips.Add(copy);
            GetBuilder<ObjectMoveBuilder>().AddAdditionalManagedClip(copy);
        }

        /**
         * There are three phases that resting state can be applied from,
         * (1) ForceObjectState, (2) Toggles and other things, (3) Toggle Rest Pose
         * Conflicts are allowed between phases, but not within a phase.
         */
        [FeatureBuilderAction(FeatureOrder.ApplyRestState1)]
        public void ApplyPendingClips() {
            foreach (var clip in pendingClips) {
                foreach (var (binding,curve) in clip.GetAllCurves()) {
                    ApplyPropertyToAvatar(binding, curve);
                    StoreBinding(binding, curve.GetFirst());
                }
            }
            pendingClips.Clear();
            stored.Clear();
        }
        [FeatureBuilderAction(FeatureOrder.ApplyRestState2)]
        public void ApplyPendingClips2() {
            ApplyPendingClips();
        }
        [FeatureBuilderAction(FeatureOrder.ApplyRestState3)]
        public void ApplyPendingClips3() {
            ApplyPendingClips();
        }

        private readonly Dictionary<EditorCurveBinding, StoredEntry> stored =
            new Dictionary<EditorCurveBinding, StoredEntry>();

        private class StoredEntry {
            public string owner;
            public FloatOrObject value;
        }

        public void StoreBinding(EditorCurveBinding binding, FloatOrObject value) {
            var owner = manager.GetCurrentlyExecutingFeatureName();
            binding = binding.Normalize();
            if (stored.TryGetValue(binding, out var otherStored)) {
                if (value != otherStored.value) {
                    throw new Exception(
                        "VRCFury was told to set the resting pose of a property to two different values.\n\n" +
                        $"Property: {binding.path} {binding.propertyName}\n\n" +
                        $"{otherStored.owner} set it to {otherStored.value}\n\n" +
                        $"{owner} set it to {value}");
                }
            }
            stored[binding] = new StoredEntry() {
                owner = owner,
                value = value
            };
        }

        private void ApplyPropertyToAvatar(EditorCurveBinding binding, FloatOrObjectCurve curve) {
            var obj = avatarObject.Find(binding.path);
            if (!obj) return;
            var component = obj.GetComponent(binding.type);
            if (!component) return;
            var val = curve.GetFirst();

            if (component is Renderer renderer && binding.propertyName.StartsWith("material.")) {
                var propName = binding.propertyName.Substring("material.".Length);
                renderer.sharedMaterials = renderer.sharedMaterials.Select(mat => {
                    if (!mat.HasProperty(propName)) return mat;
                    mat = mutableManager.MakeMutable(mat, false);
                    mat.SetFloat(propName, val.GetFloat());
                    return mat;
                }).ToArray();
                VRCFuryEditorUtils.MarkDirty(renderer);
            } else {
                var so = new SerializedObject(component);
                var prop = so.FindProperty(binding.propertyName);
                if (prop == null) return;
                if (prop.propertyType == SerializedPropertyType.ObjectReference && !val.IsFloat()) {
                    prop.objectReferenceValue = val.GetObject();
                } else if (prop.propertyType == SerializedPropertyType.Float && val.IsFloat()) {
                    prop.floatValue = val.GetFloat();
                } else if (prop.propertyType == SerializedPropertyType.Integer && val.IsFloat()) {
                    prop.intValue = (int)val.GetFloat();
                } else if (prop.propertyType == SerializedPropertyType.Boolean && val.IsFloat()) {
                    prop.boolValue = val.GetFloat() != 0;
                } else {
                    Debug.LogWarning("Failed to find property " + binding);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
