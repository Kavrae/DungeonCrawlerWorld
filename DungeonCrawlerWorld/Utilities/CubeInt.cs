namespace DungeonCrawlerWorld.Utilities
{
    /// <summary>
    /// A structure for holding 3D integer cube values as opposed to XNA's built in Cube which uses floats.
    /// Used for positioning entities within the game world grid.
    /// </summary>
    public struct CubeInt
    {
        /// <value>
        /// The position of the cube. This value represents tiles rather than pixels with exact pixel values being calculated based on the game's tile size
        /// </value>
        public Vector3Int Position { get; set; }

        /// <value>
        /// The size of the cube.This value represents tiles rather than pixels with exact pixel values being calculated based on the game's tile size
        /// </value>
        public Vector3Int Size { get; set; }

        public CubeInt( Vector3Int position )
        {
            Position = position;
            Size = new Vector3Int( 1 );
        }

        public CubeInt( Vector3Int position, Vector3Int size )
        {
            Position = position;
            Size = size;
        }
    }
}
