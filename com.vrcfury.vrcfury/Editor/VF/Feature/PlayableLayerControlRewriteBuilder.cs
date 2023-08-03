using System.Collections.Generic;
using System.Linq;
using VF.Builder;
using VF.Feature.Base;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace VF.Feature {
    /**
     * This builder handles conflicts between different users of VRCPlayableLayerControl.
     * It does this by replacing them with VRCAnimatorLayerControl that only turn on and off
     * the layers which belong to the same owner.
     */
    public class PlayableLayerControlRewriteBuilder : FeatureBuilder {
        [FeatureBuilderAction(FeatureOrder.PlayableLayerControlRewrite)]
        public void Apply() {

            var ownersByController = new Dictionary<VRCAvatarDescriptor.AnimLayerType, ISet<string>>();
            foreach (var controller in manager.GetAllUsedControllers()) {
                var type = controller.GetType();
                var uniqueOwners = new HashSet<string>();
                foreach (var layer in controller.GetLayers()) {
                    // Ignore empty layers (bask mask, junk layers, etc)
                    if (layer.stateMachine.defaultState == null) continue;
                    uniqueOwners.Add(controller.GetLayerOwner(layer));
                }
                ownersByController[type] = uniqueOwners;
            }

            foreach (var controller in manager.GetAllUsedControllers()) {
                foreach (var layer in controller.GetLayers()) {
                    var layerOwner = controller.GetLayerOwner(layer);
                    AnimatorIterator.ForEachBehaviourRW(layer, (b, add) => {
                        if (b is VRCPlayableLayerControl playableControl) {
                            var drivesTypeName = VRCFEnumUtils.GetName(playableControl.layer);
                            var drivesType = VRCFEnumUtils.Parse<VRCAvatarDescriptor.AnimLayerType>(drivesTypeName);

                            if (!ownersByController.TryGetValue(drivesType, out var uniqueOwnersOnType)) {
                                // They're driving a controller that doesn't exist?
                                // uhh... keep it I guess
                                return true;
                            }
                            if (!uniqueOwnersOnType.Contains(layerOwner)) return false;
                            if (uniqueOwnersOnType.Count == 1) return true;

                            var drivesController = manager.GetController(drivesType);
                            var drivesLayers = drivesController.GetLayers()
                                .Where(l => drivesController.GetLayerOwner(l) == layerOwner)
                                .ToList();
                            foreach (var drivesLayer in drivesLayers) {
                                var layerControl = (VRCAnimatorLayerControl)add(typeof(VRCAnimatorLayerControl));
                                layerControl.playable =
                                    VRCFEnumUtils.Parse<VRC_AnimatorLayerControl.BlendableLayer>(drivesTypeName);
                                layerControl.goalWeight = playableControl.goalWeight;
                                layerControl.blendDuration = 0;
                                layerControl.debugString = playableControl.debugString;
                                var offsetBuilder = GetBuilder<AnimatorLayerControlOffsetBuilder>();
                                offsetBuilder.Register(layerControl, drivesLayer);
                            }
                            return false;
                        }

                        return true;
                    });
                }
            }
            
            if (ownersByController.ContainsKey(VRCAvatarDescriptor.AnimLayerType.Action)
                && ownersByController[VRCAvatarDescriptor.AnimLayerType.Action].Count > 1) {
                var action = manager.GetController(VRCAvatarDescriptor.AnimLayerType.Action);
                // Make sure there's nothing on the base layer, since we won't be able to change its weight
                action.EnsureEmptyBaseLayer();
                var enableLayer = action.NewLayer("VRCF Force Enable", hasOwner: false);
                var enable = enableLayer.NewState("Enable");
                var enableControl = enable.GetRaw().VAddStateMachineBehaviour<VRCPlayableLayerControl>();
                enableControl.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
                enableControl.goalWeight = 1;
                var i = 0;
                foreach (var layer in action.GetLayers()) {
                    var layerNum = i++;
                    if (layerNum != 0) layer.weight = 0;
                }
            }
        }
    }
}