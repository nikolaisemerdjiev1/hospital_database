# Mock Hospital Database Project

**Author:** Nikolai Semerdjiev  
**Project:** Hospital Database

This project is a Java application that simulates a hospital database. It demonstrates file I/O, error handling, and console-based user interactions. All source files reside in the `MP4` package and the application uses CSV files as its data source.

---


*Note:* If you store the CSV files in a different location, update the file paths in `Main.java` accordingly.

---

## Setup and File Path Configuration

Before compiling, make sure that the CSV file paths in `Main.java` are updated to match your system. For example, this is my Windows path for each CSV.

```java
public static final String MEDICINE_CSV = "C:\\Users\\Nikolai\\Documents\\CPSC_Courses\\CPSC_231\\MP4\\Medicines.csv";
public static final String HR_CSV = "C:\\Users\\Nikolai\\Documents\\CPSC_Courses\\CPSC_231\\MP4\\FakeHRs.csv";
public static final String USER_CSV = "C:\\Users\\Nikolai\\Documents\\CPSC_Courses\\CPSC_231\\MP4\\UserCSV.csv";

```

## Compilation and Execution (Terminal Instructions)
    1. Open a terminal and navigate to the repository where MP4 is stored in.
    Example:
```bash
    cd "C:\Users\YourUsername\hospital_database"
```
    
    2. Compile Java Source Files by making sure all files are packaged and compile with:
```bash
    javac -d . MP4\*.java
```

    3. Run the Application
```bash
    java MP4.Main
```


