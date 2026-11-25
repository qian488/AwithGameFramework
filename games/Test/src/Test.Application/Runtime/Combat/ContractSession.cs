using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;

namespace Test.Application.Combat
{
    public enum ContractPhase { Opening, Camp, Loading, Battle, Result, Returning }

    public sealed class ContractSession
    {
        public const int BossId = 101;
        private const int SaveSlot = 1;
        private readonly VersionedSaveService<Completion> saves;

        public ContractSession(string saveDirectory)
        {
            saves = new VersionedSaveService<Completion>(saveDirectory, new JsonSerializer(), 1);
        }

        public ContractPhase Phase { get; private set; }
        public bool Completed { get; private set; }
        public bool SavedResult { get; private set; }
        public bool IsSaving { get; private set; }

        public async Task OpenAsync(CancellationToken token = default)
        {
            if (Phase != ContractPhase.Opening) return;
            var result = await saves.LoadAsync(SaveSlot, token);
            if (result.Status == SaveLoadStatus.Loaded && result.Data?.Completed != true)
                throw new InvalidDataException("The Rain Pass contract save is missing its completion record.");
            Completed = result.Status == SaveLoadStatus.Loaded;
            Phase = ContractPhase.Camp;
        }

        public bool TryAccept()
        {
            if (Phase != ContractPhase.Camp) return false;
            SavedResult = false;
            Phase = ContractPhase.Loading;
            return true;
        }

        public bool TryBeginBattle()
        {
            if (Phase != ContractPhase.Loading) return false;
            Phase = ContractPhase.Battle;
            return true;
        }

        public bool TryRecordVictory(RunStatus status)
        {
            if (Phase != ContractPhase.Battle || status != RunStatus.Victory) return false;
            Phase = ContractPhase.Result;
            return true;
        }

        public async Task SaveResultAsync(CancellationToken token = default)
        {
            if (Phase != ContractPhase.Result || IsSaving || SavedResult) return;
            IsSaving = true;
            try
            {
                if (!Completed)
                    await saves.SaveAsync(SaveSlot, new Completion { Completed = true }, "Rain Pass contract", token);
                Completed = true;
                SavedResult = true;
            }
            finally { IsSaving = false; }
        }

        public bool TryReturn()
        {
            if (Phase != ContractPhase.Loading && Phase != ContractPhase.Battle &&
                (Phase != ContractPhase.Result || !SavedResult)) return false;
            Phase = ContractPhase.Returning;
            return true;
        }

        public void FinishReturn()
        {
            if (Phase == ContractPhase.Returning) Phase = ContractPhase.Camp;
        }

        private sealed class Completion
        {
            public bool Completed { get; set; }
        }
    }
}
