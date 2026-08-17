using RyanAssets.DataService;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    public interface IEntity {
        string DisplayName { set; get; }
        TeamConfig Team { get; }
    }
}