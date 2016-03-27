﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MegaMan.Common;
using MegaMan.Editor.Controls.Dialogs;
using MegaMan.Editor.Controls.ViewModels.Dialogs;

namespace MegaMan.Editor.Bll.Algorithms
{
    public class TilesetImporter
    {
        public TilesetDocument Tileset { get; private set; }
        public BitmapSource Tilesheet { get; private set; }

        public List<TilesetImporterError> LastErrors { get; private set; }

        private List<WriteableBitmap> _tempTiles;
        private Dictionary<WriteableBitmap, List<SpriteFrame>> _existingFrames;

        public TilesetImporter(TilesetDocument tileset)
        {
            Tileset = tileset;
            if (Tileset != null)
            {
                _existingFrames = RipAllFrames();
                Tilesheet = SpriteBitmapCache.GetOrLoadImage(Tileset.SheetPath.Absolute);
            }
        }

        public void ExtractTiles()
        {
            _tempTiles = new List<WriteableBitmap>();
            LastErrors = new List<TilesetImporterError>();

            AddImage(Tilesheet, Tileset.SheetPath.Absolute);

            DeduplicateTemps();
            AppendNewTilesToSheet();
        }

        public void AddImages(IEnumerable<string> filePaths)
        {
            _tempTiles = new List<WriteableBitmap>();
            LastErrors = new List<TilesetImporterError>();

            var images = filePaths
                .Where(p => System.IO.File.Exists(p))
                .ToDictionary(p => p, p => new BitmapImage(new Uri(p)));

            foreach (var pair in images)
                AddImage(pair.Value, pair.Key);

            DeduplicateTemps();
            AppendNewTilesToSheet();
        }

        private void AddImage(BitmapSource image, string path)
        {
            var boxModel = new TilesetImageImportDialogViewModel(path);
            var box = new TilesetImageImportDialog();
            box.DataContext = boxModel;
            box.ShowDialog();

            if (box.Result == System.Windows.MessageBoxResult.OK)
            {
                ExtractImage(image, boxModel.Spacing, boxModel.Offset);
            }
        }

        private void ExtractImage(BitmapSource image, int spacing, int offset)
        {
            var sourceImage = BitmapFactory.ConvertToPbgra32Format(image);

            var jump = 16 + spacing;

            for (var y = offset; y < image.PixelHeight; y += jump)
            {
                for (var x = offset; x < image.PixelWidth; x += jump)
                {
                    var tileImage = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Pbgra32, null);
                    tileImage.Blit(new System.Windows.Rect(0, 0, 16, 16), sourceImage, new System.Windows.Rect(x, y, 16, 16));
                    _tempTiles.Add(tileImage);
                }
            }
        }

        private void DeduplicateTemps()
        {
            var comparer = new BitmapComparer();
            _tempTiles = _tempTiles
                .Distinct(comparer)
                .Except(_existingFrames.Keys, comparer)
                .ToList();
        }

        private Dictionary<WriteableBitmap, List<SpriteFrame>> RipAllFrames()
        {
            if (Tileset == null)
                return null;

            return Tileset.Tiles
                .SelectMany(t => t.Sprite)
                .Select(frame => new { Frame = frame, Image = SpriteBitmapCache.GetOrLoadFrame(Tileset.SheetPath.Absolute, frame.SheetLocation) })
                .GroupBy(x => x.Image)
                .ToDictionary(x => x.Key, x => x.Select(a => a.Frame).ToList());
        }

        private void AppendNewTilesToSheet()
        {
            var total = _tempTiles.Count;
            var tileWidth = Tilesheet.PixelWidth / 16;
            var addedTileHeight = (int)Math.Ceiling(total / (double)tileWidth);

            var tilesheet = new WriteableBitmap(Tilesheet.PixelWidth, Tilesheet.PixelHeight + addedTileHeight * 16, 96, 96, PixelFormats.Pbgra32, null);
            var writeableSource = BitmapFactory.ConvertToPbgra32Format(Tilesheet);
            var originalRect = new System.Windows.Rect(0, 0, Tilesheet.PixelWidth, Tilesheet.PixelHeight);
            tilesheet.Blit(originalRect, writeableSource, originalRect);

            var x = 0;
            var y = Tilesheet.PixelHeight;
            var source = new System.Windows.Rect(0, 0, 16, 16);

            foreach (var frame in _tempTiles)
            {
                var dest = new System.Windows.Rect(x, y, 16, 16);
                tilesheet.Blit(dest, frame, source);

                var tile = Tileset.AddTile();
                tile.Sprite.CurrentFrame.SetSheetPosition(x, y);

                if (x < 16 * (tileWidth - 1))
                {
                    x += 16;
                }
                else
                {
                    x = 0;
                    y += 16;
                }
            }

            Tilesheet = tilesheet;
            SpriteBitmapCache.InsertSource(Tileset.SheetPath.Absolute, tilesheet);
            Tileset.RefreshSheet();
        }

        public void CompactTilesheet()
        {
            var allFrames = RipAllFrames();
            var total = allFrames.Count;
            var root = Math.Sqrt(total);
            var width = (int)Math.Ceiling(root);
            var height = (int)Math.Ceiling(total / (double)width);

            var tilesheet = new WriteableBitmap(width * 16, height * 16, 96, 96, PixelFormats.Pbgra32, null);

            var x = 0;
            var y = 0;
            var source = new System.Windows.Rect(0, 0, 16, 16);

            foreach (var frame in allFrames)
            {
                var dest = new System.Windows.Rect(x, y, 16, 16);
                tilesheet.Blit(dest, frame.Key, source);

                foreach (var spriteFrame in frame.Value)
                {
                    spriteFrame.SetSheetPosition(x, y);
                }

                if (x < 16 * (width - 1))
                {
                    x += 16;
                }
                else
                {
                    x = 0;
                    y += 16;
                }
            }
            
            Tilesheet = tilesheet;
            SpriteBitmapCache.InsertSource(Tileset.SheetPath.Absolute, tilesheet);
            Tileset.RefreshSheet();
        }

        private class BitmapComparer : IEqualityComparer<WriteableBitmap>
        {
            public bool Equals(WriteableBitmap x, WriteableBitmap y)
            {
                var xBytes = x.ToByteArray();
                var yBytes = y.ToByteArray();

                return xBytes.SequenceEqual(yBytes);
            }

            public int GetHashCode(WriteableBitmap obj)
            {
                var array = obj.ToByteArray();

                if (array == null)
                {
                    return 0;
                }
                int hash = 17;
                foreach (var b in array)
                {
                    hash = hash * 31 + b;
                }
                return hash;
            }
        }
    }

    public class TilesetImporterError
    {
        public string FilePath { get; set; }
        public string Error { get; set; }
    }
}
