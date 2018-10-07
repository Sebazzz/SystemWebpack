/// <binding />
const path = require('path');
const targetDir = path.resolve(__dirname, 'build');

module.exports =  {
    devtool: 'inline-source-map',
    entry: {
        'main.js': ['./js/default/main.js']
    },
    output: {
        filename: '[name]',
        chunkFilename: '[name]',
        path: targetDir,
        publicPath: '/build/'
    },
    resolve: {
        alias: {
            '~': path.resolve(__dirname)
        }
    },
    module: {
        rules: [
            {
                test: /\.css$/,
                use: [
                    {
                        loader: 'style-loader'
                    },
                    {
                        loader: 'css-loader',
                        options: {
                            sourceMap: true
                        }
                    }
                ]
            }
        ]
    }
};

if (process.env.NODE_ENV) {
    module.exports.mode = process.env.NODE_ENV === 'production' ? 'production' : 'development';
}