using System.Collections.Generic;

namespace MobileUDB.Core
{
    public struct PointerContact
    {
        public int id;
        public MapPoint position;
        public PointerContact(int id, MapPoint position) { this.id = id; this.position = position; }
    }
    public enum PointerAction { None, Begin, Move, End, Cancel }
    public struct PointerStep
    {
        public PointerAction action;
        public MapPoint previous, position, previousB, positionB;
        public bool pair, navigate;
    }
    // Input adapters supply accepted map contacts and explicit releases. A missing
    // contact alone cannot commit a document operation.
    public sealed class PointerGestureState
    {
        public bool Pair { get; private set; }
        public bool Single { get; private set; }
        public int FirstId { get; private set; }
        public bool Blocked { get; private set; }
        int secondId;
        bool suppressSingle;
        MapPoint previous, previousB;
        public void CancelUntilRelease()
        {
            Pair = Single = false; suppressSingle = true; Blocked = true;
        }
        public bool AcceptFrame(bool anyPhysicalHeld)
        {
            if (!Blocked) return true;
            if (!anyPhysicalHeld) { Blocked = false; suppressSingle = false; }
            return false;
        }
        public PointerStep Advance(IReadOnlyList<PointerContact> contacts, int? releasedId, MapPoint releasePosition)
        {
            var step = new PointerStep();
            if (Blocked) return step;
            if (contacts.Count >= 2)
            {
                if (Single) { step.action = PointerAction.Cancel; Single = false; }
                PointerContact a, b;
                if (Pair && Find(contacts,FirstId,out a) && Find(contacts,secondId,out b))
                {
                    step.navigate = true; step.previous = previous; step.previousB = previousB;
                }
                else
                {
                    a = contacts[0]; b = contacts[1]; FirstId = a.id; secondId = b.id;
                }
                Pair = true; suppressSingle = true;
                previous = step.position = a.position; previousB = step.positionB = b.position;
                step.pair = true; return step;
            }
            if (Pair)
            {
                Pair = false; step.action = PointerAction.Cancel;
                if (contacts.Count == 0) suppressSingle = false;
                return step;
            }
            if (contacts.Count == 0)
            {
                if (Single)
                {
                    step.action = releasedId == FirstId ? PointerAction.End : PointerAction.Cancel;
                    step.position = releasePosition;
                }
                Single = false; suppressSingle = false; return step;
            }
            if (suppressSingle) return step;
            var contact = contacts[0];
            if (!Single)
            {
                Single = true; FirstId = contact.id; step.action = PointerAction.Begin;
            }
            else if (FirstId == contact.id) { step.action = PointerAction.Move; step.previous = previous; }
            else { Single = false; suppressSingle = true; step.action = PointerAction.Cancel; return step; }
            previous = step.position = contact.position; return step;
        }
        static bool Find(IReadOnlyList<PointerContact> contacts,int id,out PointerContact result)
        {
            for (int i = 0; i < contacts.Count; i++) if (contacts[i].id == id) { result = contacts[i]; return true; }
            result = default(PointerContact); return false;
        }
    }
}
