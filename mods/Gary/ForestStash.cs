namespace Gary
{
    // Persist only real harvested food, bounded to six items in total.
    internal readonly struct ForestStash
    {
        internal const int Capacity=6;
        internal readonly int Berries,Blueberries,Mushrooms;
        internal ForestStash(int berries,int blueberries,int mushrooms)
        {
            Berries=Clamp(berries,Capacity);
            Blueberries=Clamp(blueberries,Capacity-Berries);
            Mushrooms=Clamp(mushrooms,Capacity-Berries-Blueberries);
        }
        private static int Clamp(int value,int maximum) => System.Math.Max(0,System.Math.Min(maximum,value));
        internal int Count => Berries+Blueberries+Mushrooms;
        internal int At(int kind) => kind==0?Berries:kind==1?Blueberries:kind==2?Mushrooms:0;
        internal bool TryAdd(int kind,int amount,out ForestStash next)
        {
            next=this;if(kind<0||kind>2||amount<=0||amount>Capacity-Count)return false;
            next=new ForestStash(Berries+(kind==0?amount:0),Blueberries+(kind==1?amount:0),Mushrooms+(kind==2?amount:0));return true;
        }
        internal bool TryTake(int kind,out ForestStash next)
        {
            next=this;if(kind<0||kind>2||At(kind)<=0)return false;
            next=new ForestStash(Berries-(kind==0?1:0),Blueberries-(kind==1?1:0),Mushrooms-(kind==2?1:0));return true;
        }
    }
}
