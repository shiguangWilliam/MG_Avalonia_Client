namespace ClientAvalonia.Controls;

/// <summary>
/// Simplified continent outlines as flat [lat, lon, lat, lon, ...] arrays in
/// degrees. Retained as an offline baking tool input only — the runtime globe
/// renders the baked equirectangular texture on the GPU and carries **zero**
/// references to this data (see tactical-globe-opengl-migration-2026-08-16.md).
/// </summary>
public static class ContinentOutlines
{
    public static readonly double[][] All =
    {
        // North America: start Panama Pacific side, north up the Pacific coast,
        // east along the Arctic, down the Atlantic, around the Gulf, back along
        // the Caribbean side of Central America.
        new[]
        {
            8.2,-79.2, 9.2,-80.4, 9.9,-84.5, 10.5,-85.8, 11.5,-87.5, 13.2,-88.2,
            14.5,-91.5, 15.9,-95.5, 16.5,-98.5, 18,-103, 20.6,-105.3, 23.2,-106.5,
            26.5,-109.5, 27.9,-111, 31.7,-114.6,          // Gulf of California head
            29,-112.5, 26.5,-110.8, 24.2,-110.2,          // Baja inner side
            22.9,-109.9, 24.5,-112.1, 27,-114.2, 30,-115.8, 31.8,-116.6, 32.6,-117.1,
            33.7,-118.2, 34.4,-120.5, 37.8,-122.5, 40.4,-124.4, 46,-124, 49,-124.5,
            51,-128, 54,-130.5, 56.5,-132.5, 58,-136.5, 60,-140, 59.5,-147, 59,-152,
            58,-157.5, 56,-160.5, 55,-162.5, 58,-158.5, 60,-165, 62,-165, 64,-161,
            65,-167, 68.3,-166.8, 71.3,-156.7,            // Seward Peninsula → Barrow
            70,-146, 69.5,-134, 68.5,-125, 67,-108, 68,-100, 66,-93,
            61,-94, 56,-90, 57,-82, 61,-78,               // Hudson Bay notch
            60,-70, 58,-65, 55,-60, 51,-57, 50,-60, 45,-61, 44.5,-63.5, 44,-68,
            41.7,-70, 40.5,-73.8, 35.3,-75.5, 32,-80.8, 30.5,-81.3, 28.4,-80.5,
            26,-80, 25.2,-80.4, 26.5,-82, 28,-82.8, 30.2,-86, 30,-88.5, 29,-89.3,
            27.5,-93.5, 26,-97.2, 22,-97.8, 19,-96, 18.5,-94, 21,-90.3, 21.6,-88.2,
            19.8,-87, 17.8,-88.2, 15.9,-87.5, 14,-83.2, 12,-83.5, 10.5,-83,
            9.6,-79.5,                                     // Panama Caribbean side (closes)
        },

        // South America: from Panama, east along the Caribbean, down the Atlantic,
        // around the horn, north along the Pacific.
        new[]
        {
            8,-77.3, 11,-74.5, 11.5,-71.5, 10.5,-67, 10,-64.5, 9,-61,
            6,-58, 4,-52, 0,-50, -2.5,-41, -7,-34.5, -13,-38.5, -18,-39.5,
            -23,-42, -28,-48.5, -32,-52, -36,-57, -39,-62, -41,-65, -45,-66,
            -47,-66, -50,-69, -53.5,-72, -50,-75, -43,-75, -37,-73.5, -33,-71.6,
            -27,-71, -20,-70.3, -18.5,-70.3, -14,-76, -9,-78.5, -6,-81, -4,-81,
            -1,-80.5, 3,-78.5,                              // Ecuador
        },

        // Africa: from Tangier east along the Med, Sinai and the Red Sea, around
        // the Horn, down the east coast to the Cape, up the west coast.
        new[]
        {
            35.8,-5.8, 36.8,3, 37,10, 31,17, 32,20, 31,29, 30,32.5,
            27,34.2, 22,36.5, 15,40, 12.6,43.4, 11.8,51.3, 8,49.5,
            2,45.4, -3,41, -8,40, -14,40.6, -19,34.8, -23,35.5, -28,32.4,
            -33,27.9, -34,25.6, -34.8,20, -31,18, -26,15, -22.9,14.5,
            -17,11.7, -12,13.2, -8.8,13.2, -6,12.2, 0.4,9.5, 4,9.7,
            4.3,6, 6.4,3.4, 5.6,0, 4.7,-2, 5.2,-4, 6.3,-10.8, 8.5,-13.2,
            14.7,-17.5, 18,-16, 24,-15.5, 27,-13, 30.4,-9.6, 33.6,-7.6,
        },

        // Eurasia: from Suez north through the Levant, west along the Med's north
        // shore, around Iberia, up the Atlantic and Scandinavia, east along the
        // Arctic to Chukotka, south through East Asia, the Malay peninsula, India,
        // Arabia, and back up the Red Sea to Suez. (Longitudes past 180 written
        // as 180+x to keep the trace monotonic.)
        new double[]
        {
            30,32.5, 33,35, 36.6,36.1, 36.2,33, 36.9,30.7, 37,27.5,
            40.6,22.9, 38,23.7, 37,21.5, 44,15, 45,14, 45.5,12.6,   // Greece/Adriatic
            41.8,16.2, 40.5,18.5, 39.8,18.4, 38,16.1,               // Italy heel/toe
            40.8,14.2, 41.8,12.4, 44.3,8.8, 43.3,5.4,               // Italy west/France
            41.4,2.2, 39.5,-0.3, 36.7,-4.4, 36.1,-5.6, 37,-9, 38.7,-9.4,
            41.1,-8.7, 42.9,-9.3, 43.5,-1.8, 47.3,-2.2, 48.4,-4.5,   // Iberia/Biscay/Brittany
            49.4,-1, 51,1.8, 52,4, 55.5,8.4, 57.7,10.6,             // Channel/Jutland
            54.5,14, 54,20, 56,21, 59.5,23, 60,28,                  // Baltic south
            61.5,21, 63,20, 65.5,24, 62,17.5, 59,18.5, 56,16,       // Bothnia/Sweden
            55.4,14, 58.9,11, 59.9,10.7, 62,5, 65,12, 68,15, 71,25.8, // Norway/North Cape
            69,33, 66.8,40.5, 65,38, 68,45, 69,60, 72,72, 77,104,   // White Sea/Arctic
            73,113, 72,127, 71,140, 70,152, 67,171, 66,190,          // Taymyr→Chukotka (=-170)
            64,178, 61,166, 56,163, 50.9,156.7, 54,155.5, 58,152,   // Kamchatka
            59.5,150.8, 58,140, 53,141, 47,138.5, 43,131.8,         // Okhotsk/Amur/Vladivostok
            39.2,127.4, 35,129, 37.5,126.6, 38.9,121.6, 39,118,     // Korea/Bohai
            37.4,122.7, 36,120.4, 31.2,121.5, 28,121, 24,118,       // China coast
            22.3,114.2, 21,110.5, 20.5,106.7, 16,108.3, 12.5,109.3, // Vietnam
            8.6,104.8, 10.5,103.5, 13.4,100.5, 6,102, 1.5,103.8,    // Mekong/Gulf of Thailand/Malay
            5.4,100.3, 9.5,98.5, 16.8,96, 22,90,                    // Myanmar/Bengal delta
            17,82.5, 13,80.2, 8.1,77.5, 19,72.8, 22,70, 24.8,66.9,  // India/Pakistan
            25.5,59, 27,55, 29.5,48, 24.5,51.5, 22.5,59.8,          // Persian Gulf notch/Oman
            17,54, 12.8,45, 12.6,43.4, 16,42.5, 21.5,39.2, 27.5,35, // Yemen/Red Sea east
            29.5,34.9, 27.8,34.3,                                   // Aqaba/Sinai
        },

        // Australia: clockwise from the Gulf of Carpentaria.
        new[]
        {
            -11,142, -16,140, -12,136.5, -15,135.5, -12.5,132, -11.5,130.5,
            -14.5,129.5, -17,122, -21.5,114, -25.5,113, -30,114.5,
            -33.5,119, -34.5,124, -31.5,128, -32,134, -35,138.5,
            -38.5,141.5, -38,144, -37.5,150, -32,152.5, -25,153,
            -21,149, -17,140.5,
        },

        // Greenland: clockwise from the south tip.
        new double[]
        {
            60,-45, 65,-40, 70,-27, 76,-20, 80,-17, 82,-25,
            81,-40, 78,-58, 75,-58, 72,-55, 68,-53, 63,-51,
        },

        // Madagascar.
        new[]
        {
            -12,49.5, -16,50.3, -25,47, -25.5,44.5, -19,44, -13,48,
        },

        // Antarctica: a full ring around the pole at ~-67..-72 so the clipped
        // fill renders the polar cap.
        new double[]
        {
            -70,0, -67,40, -67,80, -70,120, -67,160, -69,190,
            -71,210, -70,240, -68,270, -70,300, -72,330, -70,350,
        },
    };
}
