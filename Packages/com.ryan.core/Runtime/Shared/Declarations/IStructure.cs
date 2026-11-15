using FishNet.Object;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Shared.Declarations {
    public interface IStructure: IEntity {
        public abstract string StructureID { get; }
        public abstract string Description { get; }
        public abstract Sprite Sprite { get; }
        public abstract ulong Cost { get; }
        public abstract float Duration { get; }
    }
}