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
        public abstract bool Load();
        public abstract void Save();
        public abstract object GetValue();
        public abstract bool TrySet(object obj);
        public abstract void InitDone();
        protected string GetSaveName(){
            return $"GameSettings_" + name;
        }
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
        public override void InitDone(){
            // it can do nothing
        }
        public override bool TrySet(object new_value){
            return TrySet((T) new_value);
        }
        public override object GetValue(){
            return (object) value;
        }
        // public virtual void Update(){
        //     Debug.LogError("Update was not overrided");
        // }
        public override bool Load(){
            if (PlayerPrefs.HasKey(GetSaveName()))
                return true;
            TrySet(start);
            return false;
        }
        public override void Save(){
            // TODO
            Debug.LogError($"Save is not implemented in {this}!");
        }
        public T value {get ; protected set;}
        public T start {get ; set;}
    }
}