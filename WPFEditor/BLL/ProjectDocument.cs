﻿using System;
using System.Collections.Generic;
using System.Linq;
using MegaMan.Common;
using System.IO;
using System.Xml.Linq;
using MegaMan.IO.Xml;
using MegaMan.Editor.Mediator;

namespace MegaMan.Editor.Bll
{
    public class ProjectDocument
    {
        private IProjectFileStructure _fileStructure;
        public Project Project { get; private set; }

        private bool dirty;
        private bool Dirty
        {
            get { return dirty; }
            set
            {
                dirty = value;
            }
        }

        #region Game XML File Stuff

        private readonly Dictionary<string, Entity> entities = new Dictionary<string,Entity>();

        public IEnumerable<Entity> Entities
        {
            get { return entities.Values; }
        }

        private string BaseDir
        {
            get
            {
                return Project.BaseDir;
            }
        }

        public string Name
        {
            get { return Project.Name; }
            set
            {
                if (Project.Name == value) return;
                Project.Name = value;
                Dirty = true;
            }
        }

        public string Author
        {
            get { return Project.Author; }
            set
            {
                if (Project.Author == value) return;
                Project.Author = value;
                Dirty = true;
            }
        }

        public int ScreenWidth
        {
            get { return Project.ScreenWidth; }
            set
            {
                if (Project.ScreenWidth == value) return;
                Project.ScreenWidth = value;
                Dirty = true;
            }
        }
        public int ScreenHeight
        {
            get { return Project.ScreenHeight; }
            set
            {
                if (Project.ScreenHeight == value) return;
                Project.ScreenHeight = value;
                Dirty = true;
            }
        }

        public string MusicNsf
        {
            get { return Project.MusicNSF.Absolute; }
            set
            {
                if (Project.MusicNSF.Absolute == value) return;
                Project.MusicNSF = FilePath.FromAbsolute(value, BaseDir);
                Dirty = true;
            }
        }

        public string EffectsNsf
        {
            get { return Project.EffectsNSF.Absolute; }
            set
            {
                if (Project.EffectsNSF.Absolute == value) return;
                Project.EffectsNSF = FilePath.FromAbsolute(value, BaseDir);
                Dirty = true;
            }
        }

        public HandlerType StartHandlerType
        {
            get
            {
                if (Project.StartHandler == null)
                    return HandlerType.Scene;

                return Project.StartHandler.Type;
            }
            set
            {
                if (Project.StartHandler == null)
                {
                    Project.StartHandler = new HandlerTransfer() { Type = StartHandlerType };
                }

                Project.StartHandler.Type = value;
            }
        }

        public string StartHandlerName
        {
            get
            {
                if (Project.StartHandler == null)
                    return null;

                return Project.StartHandler.Name;
            }
            set
            {
                if (Project.StartHandler == null)
                {
                    Project.StartHandler = new HandlerTransfer() { Type = StartHandlerType };
                }

                Project.StartHandler.Name = value;
            }
        }

        #endregion

        #region GUI Editor Stuff

        private readonly Dictionary<string, StageDocument> openStages = new Dictionary<string,StageDocument>();

        public IEnumerable<string> StageNames
        {
            get
            {
                return Project.Stages.Select(info => info.Name);
            }
        }

        public IEnumerable<string> SceneNames
        {
            get
            {
                return Project.Scenes.Select(info => info.Name);
            }
        }

        public IEnumerable<string> MenuNames
        {
            get
            {
                return Project.Menus.Select(info => info.Name);
            }
        }

        #endregion

        public static ProjectDocument CreateNew(string directory)
        {
            var project = new Project()
            {
                GameFile = FilePath.FromRelative("game.xml", directory)
            };

            var p = new ProjectDocument(new ProjectFileStructure(project), project);
            return p;
        }

        public static ProjectDocument FromFile(string path)
        {
            var projectReader = new ProjectXmlReader();
            var project = projectReader.FromXml(path);
            var structure = new ProjectFileStructure(project);
            var p = new ProjectDocument(structure, project);
            p.LoadIncludes();
            return p;
        }

        public StageDocument StageByName(string name)
        {
            if (openStages.ContainsKey(name)) return openStages[name];
            foreach (var info in Project.Stages)
            {
                if (info.Name == name)
                {
                    StageDocument stage = new StageDocument(this, info.StagePath);
                    openStages.Add(name, stage);
                    return stage;
                }
            }
            return null;
        }

        public Entity EntityByName(string name)
        {
            return entities[name];
        }

        private ProjectDocument(IProjectFileStructure fileStructure, Project project)
        {
            Project = project;
            _fileStructure = fileStructure;
        }

        private void LoadIncludes()
        {
            foreach (string path in Project.Includes)
            {
                string fullpath = Path.Combine(BaseDir, path);
                XDocument document = XDocument.Load(fullpath, LoadOptions.SetLineInfo);
                foreach (XElement element in document.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "Entities":
                            LoadEntities(element);
                            break;
                    }
                }
            }
        }

        private void LoadEntities(XElement entitiesNode)
        {
            foreach (XElement entityNode in entitiesNode.Elements("Entity"))
            {
                var entity = new Entity(entityNode, BaseDir);
                entities.Add(entity.Name, entity);
            }
        }

        public StageDocument AddStage(string name)
        {
            var stagePath = _fileStructure.CreateStagePath(name);

            var stage = new StageDocument(this)
            {
                Path = stagePath,
                Name = name
            };
            
            openStages.Add(name, stage);

            var info = new StageLinkInfo { Name = name, StagePath = stagePath };
            Project.AddStage(info);

            ViewModelMediator.Current.GetEvent<StageAddedEventArgs>().Raise(this, new StageAddedEventArgs() { Stage = info });

            Dirty = true;

            return stage;
        }

        public void Save()
        {
            var writer = new ProjectXmlWriter(Project);
            writer.Write();

            foreach (var stage in openStages.Values)
                stage.Save();

            Dirty = false;
        }
    }
}
