namespace GamaEdtech.Domain.Entity
{
    using GamaEdtech.Domain;

    public class Media : BaseEntity
    {
        #region Ctors
        private Media(string fileName, string fileAddress, string contentType, MediaEntity mediaEntity, MediaType mediaType, Guid mediaEntityId)
        {
            FileName = fileName;
            FileAddress = fileAddress;
            ContentType = contentType;
            MediaEntity = mediaEntity;
            MediaEntityId = mediaEntityId;
            MediaType = mediaType;
        }
        #endregion

        #region Propeties
        public string FileName { get; private set; }
        public string FileAddress { get; private set; }
        public string ContentType { get; private set; }
        public MediaType MediaType { get; private set; }
        public MediaEntity MediaEntity { get; private set; }
        public Guid MediaEntityId { get; private set; }
        #endregion

        #region Relations
        #endregion

        #region Functionalities
        public static Media Create(string fileName, string fileAddress,
            MediaEntity mediaEntity, MediaType mediaType, Guid mediaEntityId, string contentType) =>
            new(fileName, fileAddress, contentType, mediaEntity, mediaType, mediaEntityId);
        #endregion

        #region Domain Events

        #endregion
    }

    public enum MediaType
    {
        None,
        Photo
    }
    public enum MediaEntity
    {
        Faq
    }
}
