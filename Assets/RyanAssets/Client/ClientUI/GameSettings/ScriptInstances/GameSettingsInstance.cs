using System;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public enum GameSettingType {
        IntGameSetting,
        BoolGameSetting
    };
    public abstract class GameSettingsInstance {
        // just for polymorphism
        public string name, title, description;
        public GameSettingType type;
        public abstract void Init(GameObject obj);
        public abstract void Load();
        public abstract void Save();
    }
    public abstract class GameSettingsInstance<T>: GameSettingsInstance {
        public Action<T> on_update;
        public GameSettingsInstance(GameSettingType _type){
            type = _type;
        }
        public override void Init(GameObject obj){
            value = start;
            Load();
        }
        public virtual bool TrySet(T new_value){
            // Debug.LogError("Update was not overrided");
            value = new_value;
            return true;
        }
        // public virtual void Update(){
        //     Debug.LogError("Update was not overrided");
        // }
        public override void Load(){
            value = start;
        }
        public override void Save(){
            // TODO
        }
        public T value {get ; protected set;}
        public T start {get ; private   set;}
    }
}