
[System.Serializable]
public struct InventoryData
{
    public int lightAmmo;
    public int heavyAmmo;
    public int shellAmmo;
    public int energyAmmo;
    public int explosiveAmmo;

    public InventoryData(int lightAmmo, int heavyAmmo, int shellAmmo, int energyAmmo, int explosiveAmmo)
    {
        this.lightAmmo = 0;
        this.heavyAmmo = 0;
        this.shellAmmo = 0;
        this.energyAmmo = 0;
        this.explosiveAmmo = 0;
    }
}