#!/bin/bash

PROJECT="WorldStabilizer"
KSP_BASE="${HOME}/ksp-versions"

TARGETS="1.3.1 1.5.1 1.6.1 1.7.3"

PROJECT_VERSION=$(cat GameData/${PROJECT}/$PROJECT.version|jq '.VERSION.MAJOR,.VERSION.MINOR,.VERSION.PATCH'|tr '\n' '.'|sed -e s'/\.$//')

MSBUILD="mono /usr/lib/mono/msbuild/15.0/bin/MSBuild.dll"

SOURCE_DIR=$(pwd)

for t in ${TARGETS}; do

  KSP_DIR=$(ls -d ${KSP_BASE}/ksp-${t}-{dev,vanilla} 2>/dev/null|head -1) 
  if [ "x${KSP_DIR}" != "x" ]; then
  
    echo "Building target ${t}..."
    
    TMPDIR=$(mktemp -d /tmp/kspbuild-XXXX)
    
    cp -r . ${TMPDIR}
    cd ${TMPDIR}
    
    # Patch csproj
    export ASSEMBLY_PATH=$(echo ${KSP_DIR}|sed -e 's%\/%\\%g')
    cat ${PROJECT}/${PROJECT}.csproj|\
      perl -ne '$V=$ENV{"ASSEMBLY_PATH"}; s/<HintPath>(.*\\ksp-versions\\)(ksp-[^\\]+)(\\.*)/<HintPath>$V$3/; print $_' > tmp.csproj
    mv tmp.csproj ${PROJECT}/${PROJECT}.csproj

    # Build .dll
    KSP_BUILD_VERSION=$(echo $t|sed -e s'/\.//g')
    export SolutionDir=$(pwd)
    ${MSBUILD} /p:DefineConstants="KSP_${KSP_BUILD_VERSION}"
    PACKAGE="${PROJECT}-${PROJECT_VERSION}-ksp-$t.zip"
    
    # Patch .version
    KSP_VERSION_MAJOR=$(echo $t|cut -d. -f1)
    KSP_VERSION_MINOR=$(echo $t|cut -d. -f2)
    KSP_VERSION_PATCH=$(echo $t|cut -d. -f3)

    cat GameData/${PROJECT}/$PROJECT.version|\
      jq ".KSP_VERSION.MAJOR=${KSP_VERSION_MAJOR}|.KSP_VERSION.MINOR=${KSP_VERSION_MINOR}|.KSP_VERSION.PATCH=${KSP_VERSION_PATCH}\
      |.KSP_VERSION_MIN.MAJOR=${KSP_VERSION_MAJOR}|.KSP_VERSION_MIN.MINOR=${KSP_VERSION_MINOR}|.KSP_VERSION_MIN.PATCH=0\
      |.KSP_VERSION_MAX.MAJOR=${KSP_VERSION_MAJOR}|.KSP_VERSION_MAX.MINOR=${KSP_VERSION_MINOR}|.KSP_VERSION_MAX.PATCH=99" > tmp.version
    mv tmp.version GameData/${PROJECT}/$PROJECT.version

    # Build package
    rm ${PACKAGE}
    zip -r ${PACKAGE} WorldStabilizer-LICENSE GameData -x \*~\*
    mv ${PACKAGE} ${SOURCE_DIR}
    cd ${SOURCE_DIR}
    rm -rf ${TMPDIR}
  fi

done
