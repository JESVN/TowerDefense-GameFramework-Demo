﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using Flower.Data;
using GameFramework;

namespace Flower
{
    partial class LevelControl : IReference
    {
        private LevelData levelData;
        private LevelPathManager levelPathManager;
        private WaveControl waveControl;

        private EntityLoader entityLoader;

        private int? uiMaskFormSerialId;

        private DataPlayer dataPlayer;
        private DataTower dataTower;

        private TowerData previewTowerData;
        private Entity previewTowerEntity;
        private EntityTowerPreview previewTowerEntityLogic;
        private bool isBuilding = false;
        private bool pause = false;

        private Dictionary<int, TowerInfo> dicTowerInfo;

        public LevelControl()
        {
            dicTowerInfo = new Dictionary<int, TowerInfo>();
        }

        public void OnEnter()
        {
            entityLoader = EntityLoader.Create(this);
            dataPlayer = GameEntry.Data.GetData<DataPlayer>();
            dataTower = GameEntry.Data.GetData<DataTower>();

            GameEntry.UI.OpenUIForm(EnumUIForm.UILevelMainInfoForm);
            GameEntry.UI.OpenUIForm(EnumUIForm.UITowerListForm);

            entityLoader.ShowEntity<EntityPlayer>(EnumEntity.Player, null, EntityData.Create(levelData.PlayerPosition, levelData.PlayerQuaternion));
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (isBuilding)
            {
                if (Input.GetMouseButtonDown(0) && previewTowerEntityLogic != null && previewTowerEntityLogic.CanPlace)
                {
                    previewTowerEntityLogic.TryBuildTower();
                }
                if (Input.GetMouseButtonDown(1))
                {
                    HidePreviewTower();
                }
            }
            else
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit raycastHit;
                    if (Physics.Raycast(ray, out raycastHit, float.MaxValue, LayerMask.GetMask("Towers")))
                    {
                        if (raycastHit.collider != null)
                        {
                            EntityTowerBase entityTowerBase = raycastHit.collider.gameObject.GetComponent<EntityTowerBase>();
                            if (entityTowerBase != null)
                            {
                                entityTowerBase.ShowControlForm();
                            }
                        }
                    }
                }
            }

            if (waveControl != null)
            {
                waveControl.Update(elapseSeconds, realElapseSeconds);
            }

        }

        public void ShowPreviewTower(TowerData towerData)
        {
            if (towerData == null)
                return;

            previewTowerData = towerData;
            uiMaskFormSerialId = GameEntry.UI.OpenUIForm(EnumUIForm.UIMask);


            entityLoader.ShowEntity<EntityTowerPreview>(towerData.PreviewEntityId, (entity) =>
             {
                 previewTowerEntity = entity;
                 previewTowerEntityLogic = entity.Logic as EntityTowerPreview;
                 if (previewTowerEntityLogic == null)
                 {
                     Log.Error("Entity '{0}' logic type invaild, need EntityTowerPreview", previewTowerEntity.Id);
                     return;
                 }

                 TowerLevelData towerLevelData = towerData.GetTowerLevelData(0);
                 if (towerLevelData == null)
                 {
                     Log.Error("Tower '{0}' Level '{1}' data is null.", towerData.Name, 0);
                 }

                 EntityDataRadiusVisualiser entityDataRadiusVisualiser = EntityDataRadiusVisualiser.Create(towerLevelData.Range);

                 entityLoader.ShowEntity<EntityRadiusVisualizer>(EnumEntity.RadiusVisualiser, (entityRadiusVisualizer) =>
                 {
                     GameEntry.Entity.AttachEntity(entityRadiusVisualizer, previewTowerEntity);
                 },
                 entityDataRadiusVisualiser);

                 isBuilding = true;
             },
             EntityDataTowerPreview.Create(towerData));
        }

        public void HidePreviewTower()
        {
            if (uiMaskFormSerialId != null)
                GameEntry.UI.CloseUIForm((int)uiMaskFormSerialId);

            GameEntry.Event.Fire(this, HidePreviewTowerEventArgs.Create(previewTowerData));

            if (previewTowerEntity != null)
                entityLoader.HideEntity(previewTowerEntity);

            previewTowerEntity = null;
            previewTowerData = null;

            isBuilding = false;
        }

        public void CreateTower(TowerData towerData, IPlacementArea placementArea, IntVector2 placeGrid, Vector3 position, Quaternion rotation)
        {
            if (towerData == null)
                return;

            TowerLevelData towerLevelData = towerData.GetTowerLevelData(0);

            if (dataPlayer.Energy < towerLevelData.BuildEnergy)
                return;

            dataPlayer.AddEnergy(-towerLevelData.BuildEnergy);

            Tower tower = dataTower.CreateTower(towerData.Id);

            if (tower == null)
            {
                Log.Error("Create tower fail,Tower data id is '{0}'.", towerData.Id);
                return;
            }

            entityLoader.ShowEntity(towerData.EntityId, TypeUtility.GetEntityType(tower.Type),
            (entity) =>
            {
                EntityTowerBase entityTowerBase = entity.Logic as EntityTowerBase;
                dicTowerInfo.Add(tower.SerialId, TowerInfo.Create(tower, entityTowerBase, placementArea, placeGrid));
            }
            , EntityDataTower.Create(tower, position, rotation));

            HidePreviewTower();
        }

        public void DestroyTower(int towerSerialId)
        {
            if (!dicTowerInfo.ContainsKey(towerSerialId))
                return;

            TowerInfo towerInfo = dicTowerInfo[towerSerialId];
            entityLoader.HideEntity(dicTowerInfo[towerSerialId].EntityTower.Entity);
            towerInfo.PlacementArea.Clear(towerInfo.PlaceGrid, towerInfo.Tower.Dimensions);
            dicTowerInfo.Remove(towerSerialId);
            ReferencePool.Release(towerInfo);
        }

        private void DestroyAllTower()
        {
            List<int> towerSerialIds = new List<int>(dicTowerInfo.Keys);
            for (int i = 0; i < towerSerialIds.Count; i++)
            {
                DestroyTower(towerSerialIds[i]);
            }
        }

        public void StartWave()
        {
            waveControl = WaveControl.Create(levelData.WaveDatas, levelPathManager);
            waveControl.StartWave();
        }

        public void Pause()
        {
            pause = true;

            if (waveControl != null)
                waveControl.OnPause();

            foreach (var towerInfo in dicTowerInfo.Values)
            {
                IPause iPause = towerInfo.EntityTower;
                iPause.Pause();
            }
        }

        public void Resume()
        {
            pause = false;

            if (waveControl != null)
                waveControl.OnResume();

            foreach (var towerInfo in dicTowerInfo.Values)
            {
                IPause iPause = towerInfo.EntityTower;
                iPause.Resume();
            }
        }

        public void Restart()
        {
            waveControl.OnQuick();
            ReferencePool.Release(waveControl);
            waveControl = null;

            if (pause)
            {
                Resume();
                pause = false;
            }

            DestroyAllTower();
        }

        public void Gameover(EnumGameOverType enumGameOverType, int starCount)
        {
            if (waveControl != null)
                waveControl.OnGameover();

            Pause();
            GameEntry.UI.OpenUIForm(EnumUIForm.UIGameOverForm, UIGameOverFormOpenParam.Create(levelData, enumGameOverType, starCount));
        }

        public void Quick()
        {
            if (waveControl != null)
                waveControl.OnQuick();

            if (pause)
            {
                Resume();
                pause = false;
            }

            DestroyAllTower();
            entityLoader.HideAllEntity();
        }

        public static LevelControl Create(LevelData levelData, LevelPathManager levelPathManager)
        {
            LevelControl levelControl = ReferencePool.Acquire<LevelControl>();
            levelControl.levelData = levelData;
            levelControl.levelPathManager = levelPathManager;
            return levelControl;
        }

        public void Clear()
        {
            levelData = null;
            levelPathManager = null;

            if (waveControl != null)
                ReferencePool.Release(waveControl);

            waveControl = null;

            if (entityLoader != null)
                ReferencePool.Release(entityLoader);

            entityLoader = null;

            uiMaskFormSerialId = null;

            dataPlayer = null;
            dataTower = null;

            previewTowerData = null;
            previewTowerEntity = null;
            previewTowerEntityLogic = null;
            isBuilding = false;

            dicTowerInfo.Clear();
        }
    }
}