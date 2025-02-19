namespace DungeonCrawlerWorld.Utilities
{
    public struct CubeInt
    {
        public Vector3Int Position { get; set; }

        public Vector3Int Size { get; set; }

        public CubeInt(Vector3Int position, Vector3Int size)
        {
            Position = position;
            Size = size;
        }
    }
}
