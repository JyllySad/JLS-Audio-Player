using System.IO;

namespace JLS.Services
{
    public class SafeFileAbstraction : TagLib.File.IFileAbstraction
    {
        public string Name { get; private set; }

        public Stream ReadStream => new FileStream(Name, FileMode.Open, FileAccess.Read, FileShare.Read);

        public Stream WriteStream => new FileStream(Name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        public void CloseStream(Stream stream) => stream.Close();

        public SafeFileAbstraction(string file)
        {
            Name = PathHelper.GetSafePath(file);
        }
    }
}